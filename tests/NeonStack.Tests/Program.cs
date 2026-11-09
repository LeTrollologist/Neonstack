using NeonStack.Tests;

// The harness writes settings/scores/progress through the real SaveData path, so point it at a
// throwaway folder before anything touches the persistent types.
string saveDir = Path.Combine(Path.GetTempPath(), "neonstack-tests-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("NEONSTACK_SAVEDIR", saveDir);

int exitCode = Harness.Run(typeof(Harness).Assembly);

try { Directory.Delete(saveDir, recursive: true); } catch { /* best effort */ }
return exitCode;
