using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace cs2_rockthevote
{
    public enum CastVote
    {
        VOTE_NOTINCLUDED = -1,
        VOTE_OPTION1,  // Use this for Yes
        VOTE_OPTION2,  // Use this for No
        VOTE_OPTION3,
        VOTE_OPTION4,
        VOTE_OPTION5,
        VOTE_UNCAST = 5
    }

    public enum YesNoVoteEndReason
    {
        VoteEnd_AllVotes,  // All possible votes were cast
        VoteEnd_TimeUp,    // Time ran out
        VoteEnd_Cancelled  // The vote got cancelled
    }

    public enum YesNoVoteAction
    {
        VoteAction_Start,  // nothing passed
        VoteAction_Vote,   // param1 = client slot, param2 = choice (VOTE_OPTION1 = yes, VOTE_OPTION2 = no)
        VoteAction_End     // param1 = YesNoVoteEndReason reason why the vote ended
    }

    public class YesNoVoteInfo
    {
        public int num_votes;                // Number of votes tallied in total
        public int yes_votes;                // Number of votes for yes
        public int no_votes;                 // Number of votes for no
        public int num_clients;              // Number of clients who could vote
        //public int[,] clientInfo = new int[MAXPLAYERS, 2];  // Client voting info, user VOTEINFO_CLIENT_ defines. Anything >= [num_clients] is VOTE_NOTINCLUDED, VOTE_UNCAST = client didn't vote
        public Dictionary<int, (int, int)> clientInfo = new Dictionary<int, (int, int)>();
    }

    public class VoteConstants
    {
        public const int VOTE_CALLER_SERVER = 99;
        public const int VOTE_NOTINCLUDED = -1;
        public const int VOTE_UNCAST = 5;
        public const int MAXPLAYERS = 64;
    }

    public delegate bool YesNoVoteResult(YesNoVoteInfo info);
    public delegate void YesNoVoteHandler(YesNoVoteAction action, int param1, int param2);

    public static class PanoramaVote
    {
        private static int m_iVoteCount = 0;
        private static bool m_bIsVoteInProgress = false;
        private static YesNoVoteHandler? m_VoteHandler = null;
        private static YesNoVoteResult? m_VoteResult = null;
        private static int m_iVoterCount;
        private static int[] m_iVoters = new int[VoteConstants.MAXPLAYERS];
        private static int m_iCurrentVoteCaller;
        private static string m_szCurrentVoteTitle = string.Empty;
        private static string m_szCurrentVoteDetailStr = string.Empty;
        public static CVoteController? VoteController { get; private set; } = null;
        private static RecipientFilter CurrentVotefilter = new RecipientFilter();
        private static Timer? m_VoteTimer = null;
        private static ILogger _debugLogger = NullLogger.Instance;

        /// Sets the debug logger used for diagnostic messages (gated by DebugLogging)
        public static void SetDebugLogger(ILogger logger) => _debugLogger = logger;

        /// Resets the vote state, clearing any ongoing vote information.
        public static void Reset(CVoteController? voteController = null)
        {
            m_VoteTimer?.Kill();
            m_VoteTimer = null;
            m_bIsVoteInProgress = false;
            m_VoteHandler = null;
            m_VoteResult = null;
            m_szCurrentVoteTitle = string.Empty;
            m_szCurrentVoteDetailStr = string.Empty;

            if(voteController != null)
            {
                for (int i = 0; i < VoteConstants.MAXPLAYERS; i ++)
                {
                    voteController.VotesCast[i] = (int)CastVote.VOTE_UNCAST;
                }

                voteController.VoteOptionCount[0] = 0;
                voteController.VoteOptionCount[1] = 0;
                voteController.VoteOptionCount[2] = 0;
                voteController.VoteOptionCount[3] = 0;
                voteController.VoteOptionCount[4] = 0;
            }
        }

        /// Clears all vote states on map change.
        public static void OnMapStart()
        {
            m_VoteTimer?.Kill();
            m_VoteTimer = null;
            m_bIsVoteInProgress = false;
            m_VoteHandler = null;
            m_VoteResult = null;
            m_szCurrentVoteTitle = string.Empty;
            m_szCurrentVoteDetailStr = string.Empty;
            VoteController = null;
        }

        /// Initializes the vote controller if a vote is not already in progress.
        public static void Init()
        {
            if (m_bIsVoteInProgress)
                return;

            var controllers = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller");
            var pVoteController = controllers.LastOrDefault();
            if (pVoteController == null)
                return;

            VoteController = pVoteController;
        }

        /// Handles the event when a vote is cast by a player.
        public static void VoteCast(GameEvent pEvent)
        {
            if (VoteController == null || !m_bIsVoteInProgress)
                return;

            if (m_VoteHandler != null)
            {
                var pVoter = new CCSPlayerController(NativeAPI.GetEventPlayerController(pEvent.Handle,"userid"));
                if (pVoter == null) return;
                m_VoteHandler(YesNoVoteAction.VoteAction_Vote, pVoter.Slot, NativeAPI.GetEventInt(pEvent.Handle, "vote_option"));
            }
            
            UpdateVoteCounts();
            CheckForEarlyVoteClose();
        }

        /// Removes a player from the current vote.
        public static void RemovePlayerFromVote(int iSlot)
        {
            if (!m_bIsVoteInProgress) return;

            bool found = false;
            for (int i = 0; i < m_iVoterCount; i++)
            {
                if (m_iVoters[i] == iSlot)
                    found = true;
                else if (found)
                    m_iVoters[i - 1] = m_iVoters[i];
            }

            if (found)
            {
                m_iVoterCount--;
                m_iVoters[m_iVoterCount] = -1;
            }
        }

        /// Checks if a player is in the current vote pool.
        public static bool IsPlayerInVotePool(int iSlot)
        {
            if (!m_bIsVoteInProgress)
                return false;

            if (iSlot < 0 || iSlot > m_iVoterCount)
                return false;

            return m_iVoters.Contains(iSlot);
        }

        /// Updates the vote counts and fires a vote changed event.
        public static void UpdateVoteCounts()
        {
            if(VoteController == null)return;
            
            var pEventPtr = NativeAPI.CreateEvent("vote_changed", true);

            NativeAPI.SetEventInt(pEventPtr, "vote_option1", VoteController.VoteOptionCount[0]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option2", VoteController.VoteOptionCount[1]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option3", VoteController.VoteOptionCount[2]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option4", VoteController.VoteOptionCount[3]);
            NativeAPI.SetEventInt(pEventPtr, "vote_option5", VoteController.VoteOptionCount[4]);
            NativeAPI.SetEventInt(pEventPtr, "potentialVotes", m_iVoterCount);

            NativeAPI.FireEvent(pEventPtr, false);
        }

        /// Checks if a vote is currently in progress.
        public static bool IsVoteInProgress()
        {
            return m_bIsVoteInProgress;
        }

        /// Start a new Yes/No vote for all players
        public static bool SendYesNoVoteToAll(float flDuration, int iCaller, string sVoteTitle, string sDetailStr, YesNoVoteResult resultCallback, YesNoVoteHandler? handler = null)
        {
            CurrentVotefilter.Clear();
            foreach(var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.Connected))
            {
                CurrentVotefilter.Add(player);
            }

            return SendYesNoVote(flDuration, iCaller, sVoteTitle, sDetailStr, CurrentVotefilter, resultCallback, handler);
        }
        
        /// Starts a new Yes/No vote for Specific Players.
        public static bool SendYesNoVote(float flDuration, int iCaller, string sVoteTitle, string sDetailStr, RecipientFilter pFilter, YesNoVoteResult resultCallback, YesNoVoteHandler? handler = null)
        {
            if(VoteController == null)
                return false;

            if (m_bIsVoteInProgress)
            {
                _debugLogger.LogWarning("[PanoramaVote] A vote is already in progress.");
                return false;
            }

            if (pFilter.Count <= 0)
                return false;

            if (resultCallback == null)
                return false;

            Reset(VoteController);

            m_bIsVoteInProgress = true;
            CurrentVotefilter = pFilter;
            InitVoters(pFilter);

            VoteController.PotentialVotes = m_iVoterCount;
            VoteController.ActiveIssueIndex = 2;

            m_VoteResult = resultCallback;
            m_VoteHandler = handler;

            m_iCurrentVoteCaller = iCaller;
            m_szCurrentVoteTitle = sVoteTitle;
            m_szCurrentVoteDetailStr = sDetailStr;

            UpdateVoteCounts();
            SendVoteStartUM(pFilter);

            m_VoteHandler?.Invoke(YesNoVoteAction.VoteAction_Start, 0, 0);

            int voteNum = m_iVoteCount;
            m_VoteTimer?.Kill();
            m_VoteTimer = new Timer(flDuration, () =>
            {
                m_VoteTimer = null;
                if (voteNum == m_iVoteCount)
                    EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
            }, TimerFlags.STOP_ON_MAPCHANGE);

            return true;
        }
        
        /// Sends a user message to start the vote.
        private static void SendVoteStartUM(RecipientFilter pFilter)
        {
            UserMessage voteStart = UserMessage.FromId(346);
            voteStart.SetInt("team", -1);
            voteStart.SetInt("player_slot", m_iCurrentVoteCaller);
            voteStart.SetInt("vote_type", -1);  // -1 = csgo logo icon, 1 = Change a map icon
            voteStart.SetString("disp_str", m_szCurrentVoteTitle);
            voteStart.SetString("details_str", m_szCurrentVoteDetailStr);
            voteStart.SetBool("is_yes_no_vote", true);

            voteStart.Send(pFilter);
        }

        /// Initializes the voters for the current vote.
        private static void InitVoters(RecipientFilter pFilter)
        {
            m_iVoterCount = 0;
            for (int i = 0; i < VoteConstants.MAXPLAYERS; i++)
            {
                m_iVoters[i] = -1;
            }

            m_iVoterCount = pFilter.Count;
            for (int i = 0, j = 0; i < m_iVoterCount; i++)
            {
                if (pFilter[i].Slot != -1)
                {
                    m_iVoters[j] = pFilter[i].Slot;
                    j++;
                }
            }
        }

        /// Checks if the vote can be closed early based on the number of votes cast.
        private static void CheckForEarlyVoteClose()
        {
            if(VoteController == null)return;

            int votes = VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION1] + VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION2];
            if (votes >= m_iVoterCount)
            {
                Server.NextFrame(() => EndVote(YesNoVoteEndReason.VoteEnd_AllVotes));
            }
        }

        /// Cancels the current vote.
        public static void CancelVote()
        {
            if (!m_bIsVoteInProgress)
                return;

            EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
        }
        
        /// Ends the current vote with the specified reason.
        public static void EndVote(YesNoVoteEndReason reason, int overrideFailCode = -1)
        {
            if (!m_bIsVoteInProgress)
                return;

            m_bIsVoteInProgress = false;

            m_VoteTimer?.Kill();
            m_VoteTimer = null;

            if (m_iVoteCount == 99)
                m_iVoteCount = 0;
            else
                m_iVoteCount++;

            if (m_VoteHandler != null)
                m_VoteHandler(YesNoVoteAction.VoteAction_End, (int)reason, 0);

            if (VoteController == null)
            {
                SendVoteFailed(reason, overrideFailCode);
                return;
            }

            if (m_VoteResult == null || reason == YesNoVoteEndReason.VoteEnd_Cancelled)
            {
                SendVoteFailed(reason, overrideFailCode);
                VoteController.ActiveIssueIndex = -1;
                return;
            }

            var info = new YesNoVoteInfo
            {
                num_clients = m_iVoterCount,
                yes_votes   = VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION1],
                no_votes    = VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION2],
                num_votes   = VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION1]
                            + VoteController.VoteOptionCount[(int)CastVote.VOTE_OPTION2]
            };

            for (int i = 0; i < CurrentVotefilter.Count; i++)
            {
                if (i < m_iVoterCount)
                    info.clientInfo[i] = (m_iVoters[i], VoteController.VotesCast[m_iVoters[i]]);
                else
                    info.clientInfo[i] = (-1, -1);
            }

            bool passed = m_VoteResult(info);
            if (passed)
                SendVotePassed("#SFUI_vote_passed", "Vote Passed!");
            else
                SendVoteFailed(YesNoVoteEndReason.VoteEnd_TimeUp, 4); 
                // 0 = "Vote Failed." in pano ui descripiton. 4 = Not enough players voted. 5 = Server has disabled that issue.

        }

        private static void SendVoteFailed(YesNoVoteEndReason reason, int failCodeOverride = -1)
        {
            UserMessage voteFailed = UserMessage.FromId(348);
            voteFailed.SetInt("team", -1);

            int codeToSend = failCodeOverride >= 0 ? failCodeOverride : (int)reason;
            voteFailed.SetInt("reason", codeToSend);

            var pFilter = new RecipientFilter();
            pFilter.AddAllPlayers();
            voteFailed.Send(pFilter);
        }

        /// Sends a user message indicating that the vote passed.
        private static void SendVotePassed(string disp_str = "", string details_str = "")
        {
            UserMessage votePass = UserMessage.FromId(347);

            votePass.SetInt("team", -1);
            votePass.SetInt("vote_type", 2);
            votePass.SetString("disp_str", disp_str);
            votePass.SetString("details_str", details_str);

            RecipientFilter pFilter = new RecipientFilter();
            pFilter.AddAllPlayers();

            votePass.Send(pFilter);
        }
    }
}