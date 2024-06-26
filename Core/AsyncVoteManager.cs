namespace cs2_rockthevote
{
    public record VoteResult(VoteResultEnum Result, int VoteCount, int RequiredVotes);

    public class AsyncVoteManager
    {
        private List<int> votes = new();
        private readonly IVoteConfig _config;
        private readonly AsyncVoteValidator _voteValidator;
        private int totalPlayers;

        public AsyncVoteManager(IVoteConfig config)
        {
            _config = config;
            _voteValidator = new AsyncVoteValidator(config);
        }

        public int VoteCount => votes.Count;
        public int RequiredVotes => _voteValidator.RequiredVotes(totalPlayers);

        public bool VotesAlreadyReached { get; set; } = false;

        public void OnMapStart(string _mapName, int playerCount)
        {
            votes.Clear();
            VotesAlreadyReached = false;
            totalPlayers = playerCount;
        }

        public VoteResult AddVote(int userId)
        {
            if (VotesAlreadyReached)
                return new VoteResult(VoteResultEnum.VotesAlreadyReached, VoteCount, RequiredVotes);

            VoteResultEnum? result = null;
            if (votes.IndexOf(userId) != -1)
                result = VoteResultEnum.AlreadyAddedBefore;
            else
            {
                votes.Add(userId);
                result = VoteResultEnum.Added;
            }

            if (_voteValidator.CheckVotes(votes.Count, totalPlayers))
            {
                VotesAlreadyReached = true;
                return new VoteResult(VoteResultEnum.VotesReached, VoteCount, RequiredVotes);
            }

            return new VoteResult(result.Value, VoteCount, RequiredVotes);
        }

        public void RemoveVote(int userId)
        {
            var index = votes.IndexOf(userId);
            if (index > -1)
                votes.RemoveAt(index);
        }
    }
}
