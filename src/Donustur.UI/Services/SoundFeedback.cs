namespace Donustur.UI.Services;

public static class SoundFeedback
{
    public static void PlayTick()
    {
        try
        {
            System.Media.SystemSounds.Beep.Play();
        }
        catch
        {
            // Ses çalınamazsa sessizce geç
        }
    }

    public static void PlaySuccess()
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Sessizce geç
        }
    }
}
