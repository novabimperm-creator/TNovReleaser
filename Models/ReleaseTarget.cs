using System.Collections.Generic;

namespace TNovReleaser.Models
{
    public sealed class ReleaseTarget
    {
        public ReleaseTarget(string name, string sourceDirectory, IList<string> outputDirectories)
        {
            Name = name;
            SourceDirectory = sourceDirectory;
            OutputDirectories = outputDirectories;
        }

        public string Name { get; private set; }
        public string SourceDirectory { get; private set; }
        public IList<string> OutputDirectories { get; private set; }
    }
}
