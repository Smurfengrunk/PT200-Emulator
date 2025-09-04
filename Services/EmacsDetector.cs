using PT200Emulator.Util;
using System.Text;

public class EmacsDetector
{
    private readonly StringBuilder buffer = new();
    private bool detectionCompleted = false;

    public bool IsReady { get; private set; } = false;
    public bool EmacsMode { get; private set; } = false;

    public void Feed(char ch)
    {
        if (detectionCompleted)
            return;

        // Samla rad 0 (eller första 80 tecken)
        buffer.Append(ch);
        if (buffer.Length > 512)
            buffer.Remove(0, buffer.Length - 512); // håll buffer liten

        string text = buffer.ToString();

        // Enkel heuristik: leta efter "Welcome to the Prime Computer"
        if (text.Contains("Initializing Emacs"))
        {
            EmacsMode = true;
            IsReady = true;
            detectionCompleted = true;

            Logger.Log("[EMACS-DETECTOR] EMACS-terminal identifierad");
        }
    }

    public void Reset()
    {
        buffer.Clear();
        IsReady = false;
        EmacsMode = false;
        detectionCompleted = false;
    }
}