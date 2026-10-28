namespace NeonStack;

public enum IntroScreen { Audax, Brutal, Finished }

/// <summary>
/// Startup sequence: Audax Softworks ident, then the BRUTAL logo (required by the BRUTAL licence,
/// section 2.2), then the title screen. Enter/Space advances, Esc skips to the title.
/// </summary>
public sealed class Intro
{
    const float AudaxDuration = 2.8f;
    const float BrutalDuration = 3.0f;
    const float FadeIn = 0.55f;

    readonly bool[] prev = new bool[(int)Key.Count];

    public IntroScreen Screen { get; private set; } = IntroScreen.Audax;
    public float T { get; private set; }
    public float Fade { get; private set; }

    public bool Active => Screen != IntroScreen.Finished;

    float Duration => Screen == IntroScreen.Audax ? AudaxDuration : BrutalDuration;

    public void Update(float dt, bool[] down)
    {
        if (!Active) return;
        T += dt;

        bool confirm = down[(int)Key.Enter] || down[(int)Key.Space];
        bool confirmPrev = prev[(int)Key.Enter] || prev[(int)Key.Space];
        bool skip = down[(int)Key.Escape] && !prev[(int)Key.Escape];

        for (int i = 0; i < prev.Length; i++) prev[i] = down[i];

        if (skip) { Screen = IntroScreen.Finished; return; }
        if ((confirm && !confirmPrev && T > 0.8f) || T >= Duration) Next();
        if (!Active) return;

        float inK = Smooth(0f, FadeIn, T);
        float outK = 1f - Smooth(Duration - 0.35f, Duration, T);
        Fade = Math.Clamp(Math.Min(inK, outK), 0f, 1f);
    }

    void Next()
    {
        Screen = Screen == IntroScreen.Audax ? IntroScreen.Brutal : IntroScreen.Finished;
        T = 0f;
        Fade = 0f;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
