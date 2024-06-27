namespace cs2_rockthevote
{
    public class AsyncVoteValidator
    {
        private float VotePercentage = 0F;
        private IVoteConfig _config { get; set; }
        private int totalPlayers = ServerManager.ValidPlayerCount();

        public AsyncVoteValidator(IVoteConfig config)
        {
            _config = config;
            VotePercentage = _config.VotePercentage / 100F;
        }

        public int RequiredVotes(int totalPlayers)
        {
            return (int)Math.Ceiling(totalPlayers * VotePercentage);
        }

        public bool CheckVotes(int numberOfVotes, int totalPlayers)
        {
            return numberOfVotes > 0 && numberOfVotes >= RequiredVotes(totalPlayers);
        }
    }
}
