namespace cs2_rockthevote
{
    public record VoteResult(VoteResultEnum Result, int VoteCount, int RequiredVotes);

    public class AsyncVoteManager
    {
        private readonly List<int> votes = new();
        private readonly AsyncVoteValidator _voteValidator;

        public int VoteCount => votes.Count;
        public int RequiredVotes => _voteValidator.RequiredVotes(ServerManager.ValidPlayerCount());
        public bool VotesAlreadyReached { get; private set; } = false;
        public IReadOnlyCollection<int> Voters => votes.AsReadOnly();

        public AsyncVoteManager(int votePercentage)
        {
            _voteValidator = new AsyncVoteValidator(votePercentage / 100f);
        }

        public void OnMapStart(string _mapName)
        {
            votes.Clear();
            VotesAlreadyReached = false;
        }

        public VoteResult AddVote(int userId, int? totalPlayersOverride = null)
        {
            int totalPlayers = totalPlayersOverride ?? ServerManager.ValidPlayerCount();
            int requiredVotes = _voteValidator.RequiredVotes(totalPlayers);

            if (VotesAlreadyReached)
                return new VoteResult(VoteResultEnum.VotesAlreadyReached, VoteCount, requiredVotes);

            VoteResultEnum result;
            if (votes.Contains(userId))
                result = VoteResultEnum.AlreadyAddedBefore;
            else
            {
                votes.Add(userId);
                result = VoteResultEnum.Added;
            }

            if (_voteValidator.CheckVotes(votes.Count, totalPlayers))
            {
                VotesAlreadyReached = true;
                return new VoteResult(VoteResultEnum.VotesReached, VoteCount, requiredVotes);
            }

            return new VoteResult(result, VoteCount, requiredVotes);
        }

        public void RemoveVote(int userId)
        {
            votes.Remove(userId);
        }
    }
}