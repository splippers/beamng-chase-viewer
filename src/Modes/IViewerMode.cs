namespace BeamQuest.Modes
{
    /// <summary>
    /// All three viewer modes (Spectator, Cockpit, Replay) implement this interface.
    /// The application swaps modes at runtime without rebuilding the render pipeline.
    /// </summary>
    public interface IViewerMode
    {
        string Name { get; }

        void Activate();
        void Deactivate();

        /// <summary>Called once per frame with the render-loop delta time.</summary>
        void Tick(float dt);

        /// <summary>Returns the view matrix for the current mode's camera.</summary>
        Matrix4x4 GetViewMatrix(int eye);

        /// <summary>Returns the world-space camera position (used for audio, LOD, culling).</summary>
        Vector3 CameraPosition { get; }
    }
}
