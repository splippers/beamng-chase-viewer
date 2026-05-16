namespace BeamQuest.Protocol
{
    /// <summary>
    /// Abstraction over all data sources: BeamNG UDP mod, BeamMP WebSocket plugin,
    /// OutGauge, and replay files.  Consumers call <see cref="ReadFramesAsync"/>
    /// to get a continuous stream of world frames.
    /// </summary>
    public interface IVehicleDataSource : IAsyncDisposable
    {
        string Name { get; }
        bool   IsConnected { get; }

        Task<bool> ConnectAsync(CancellationToken ct = default);
        void       Disconnect();

        /// <summary>
        /// Yields successive WorldFrames as they arrive.
        /// Completes when disconnected or <paramref name="ct"/> is cancelled.
        /// </summary>
        IAsyncEnumerable<WorldFrame> ReadFramesAsync(CancellationToken ct = default);
    }
}
