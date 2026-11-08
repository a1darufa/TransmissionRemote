namespace TransmissionKit.Tests;

/// <summary>
/// Element-by-element comparison with CollectionAssert's failure messages, so tests can list the expected
/// items inline instead of building an array at every call.
/// </summary>
internal static class SequenceAssert
{
    public static void AreEqual<T>(IEnumerable<T> actual, params ReadOnlySpan<T> expected) =>
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
}
