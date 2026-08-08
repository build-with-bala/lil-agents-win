namespace LilAgents.Rendering;

/// <summary>
/// Maps elapsed animation time to normalised travel distance (0 to 1).
///
/// A direct port of the macOS build's <c>movementPosition(at:)</c>, and it must stay a
/// direct port: the timings were derived from frame-by-frame analysis of the walk videos,
/// so the character's feet only look planted if the position curve matches the footfalls
/// in the sprite frames. The four phases are stand-still, ease-in, constant speed, ease-out.
///
/// Velocity is normalised so the integral over the whole walk equals exactly 1:
/// the ease phases each contribute half their duration at full speed.
/// </summary>
public readonly record struct MovementCurve(
    double AccelStart,
    double FullSpeedStart,
    double DecelStart,
    double WalkStop)
{
    public double Evaluate(double videoTime)
    {
        var easeInDuration = FullSpeedStart - AccelStart;
        var linearDuration = DecelStart - FullSpeedStart;
        var easeOutDuration = WalkStop - DecelStart;

        var denominator = easeInDuration / 2.0 + linearDuration + easeOutDuration / 2.0;
        if (denominator <= 0) return videoTime >= WalkStop ? 1.0 : 0.0;

        var velocity = 1.0 / denominator;

        if (videoTime <= AccelStart) return 0.0;

        if (videoTime <= FullSpeedStart)
        {
            if (easeInDuration <= 0) return 0.0;
            var t = videoTime - AccelStart;
            return velocity * t * t / (2.0 * easeInDuration);
        }

        var easeInDistance = velocity * easeInDuration / 2.0;

        if (videoTime <= DecelStart)
        {
            var t = videoTime - FullSpeedStart;
            return easeInDistance + velocity * t;
        }

        if (videoTime <= WalkStop)
        {
            if (easeOutDuration <= 0) return 1.0;
            var linearDistance = velocity * linearDuration;
            var t = videoTime - DecelStart;
            return easeInDistance + linearDistance + velocity * (t - t * t / (2.0 * easeOutDuration));
        }

        return 1.0;
    }
}
