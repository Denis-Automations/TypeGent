namespace TypeGent.Core.Layouts;

/// <summary>Which hand types a key.</summary>
public enum Hand { Left, Right }

/// <summary>
/// Which finger types a key (thumb excluded — it hits only the space bar).
/// Index = 1, Middle = 2, Ring = 3, Pinky = 4.
/// </summary>
public enum Finger { Index = 1, Middle = 2, Ring = 3, Pinky = 4 }

/// <summary>
/// Physical metadata for one key on a keyboard layout. Used by <see cref="DelayModel"/> in v2
/// Phase 3 to derive a biomechanical timing multiplier from the prev→current key relationship.
/// <para>
/// <c>X</c> and <c>Y</c> are in abstract key-width units (each key is 1 unit wide); row offsets
/// match the standard QWERTY stagger (home row Y = 0, top row Y = 1, bottom row Y = −1).
/// </para>
/// </summary>
public readonly record struct KeyMeta(Hand Hand, Finger Finger, int Row, double X, double Y);
