// Framework-free self-checks:
//
//   dotnet run --project tests
//
// Covers the wire codecs, where a wrong answer is silently believed. Anything
// that needs a gateway on the other end is verified against hardware.

using industREAL.CAN.CANGW.Tests;

try
{
    CobsTests.RunAll();
    EnvelopeTests.RunAll();
    Console.WriteLine("industREAL.CAN.CANGW self-checks: ALL PASSED");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"industREAL.CAN.CANGW self-checks: FAILED\n  {ex.Message}");
    return 1;
}
