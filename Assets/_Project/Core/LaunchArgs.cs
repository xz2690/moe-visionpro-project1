using System;

namespace MR.Core
{
    /// <summary>
    /// Command-line flags with an environment-variable twin, because visionOS apps receive
    /// no launch arguments: "-say hello" on the command line is MR_SAY=hello in the
    /// environment (xcrun simctl launch passes SIMCTL_CHILD_MR_SAY through).
    /// </summary>
    public static class LaunchArgs
    {
        public static string Get(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-" + flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];

            string env = Environment.GetEnvironmentVariable("MR_" + flag.ToUpperInvariant());
            return string.IsNullOrEmpty(env) ? null : env;
        }

        /// <summary>"-flag" present, or MR_FLAG set to 1 / true / yes.</summary>
        public static bool Flag(string flag)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, "-" + flag, StringComparison.OrdinalIgnoreCase)) return true;

            string env = Environment.GetEnvironmentVariable("MR_" + flag.ToUpperInvariant());
            return env != null && (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                   env.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
    }
}
