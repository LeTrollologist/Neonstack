using System.Reflection;

namespace NeonStack.Tests;

/// <summary>Marks a public static void method as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

/// <summary>Thrown when an assertion fails; the runner reports the message and fails the run.</summary>
public sealed class CheckException : Exception
{
    public CheckException(string message) : base(message) { }
}

/// <summary>
/// Minimal zero-dependency test runner: discovers <see cref="TestAttribute"/> methods, runs them in
/// a stable order and returns a process exit code. Keeps the repository testable without pulling in a
/// test-framework package.
/// </summary>
public static class Harness
{
    public static int Run(Assembly assembly)
    {
        var tests = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} tests...\n");

        foreach (var method in tests)
        {
            string name = method.DeclaringType!.Name + "." + method.Name;
            try
            {
                method.Invoke(null, null);
                passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (TargetInvocationException e)
            {
                string detail = e.InnerException is CheckException ? e.InnerException.Message
                              : e.InnerException?.ToString() ?? e.Message;
                failures.Add(name + " - " + detail);
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + detail.Replace("\n", "\n        "));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{tests.Count} passed, {failures.Count} failed");
        foreach (string failure in failures) Console.WriteLine("  ! " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new CheckException("expected true: " + what);
    }

    public static void False(bool condition, string what)
    {
        if (condition) throw new CheckException("expected false: " + what);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new CheckException($"{what}: expected {expected}, got {actual}");
    }

    public static void InRange(int value, int min, int max, string what)
    {
        if (value < min || value > max) throw new CheckException($"{what}: {value} not in {min}..{max}");
    }
}
