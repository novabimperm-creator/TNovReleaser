using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    /// <summary>Результат проверки перед выпуском.</summary>
    internal sealed class DependencyCheckResult
    {
        public List<string> Problems = new List<string>();

        /// <summary>Модули из actual, которые сломаются, а их версия из build совместима — их надо выпустить вместе.</summary>
        public SortedSet<string> ReleaseTogether = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Выпускаемые сборки, в которых не хватает того, что используют модули в actual (например TNovCommon).</summary>
        public SortedSet<string> BrokenSelected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что после выпуска все TNov*-сборки в каждом каталоге actual совместимы между собой:
    /// и выпускаемые с оставшимися (TNov.dll 1006 → TNovUtils 928 без ModelSyncService),
    /// и оставшиеся с выпускаемыми (TNovUtilsAR 0925 → TNovCommon без LevelNumberSettings.maxOffset).
    /// Версии сборок не сравниваются: каждая сборка поднимает версии всех плагинов, а Revit 2022 их не сверяет.
    /// </summary>
    internal static class DependencyCheckService
    {
        public static DependencyCheckResult Check(IList<FileItem> files, IEnumerable<ReleaseTarget> targets)
        {
            var result = new DependencyCheckResult();
            var selectedFiles = files.Where(file => file.Selected).Select(file => file.FileName).ToArray();

            foreach (var target in targets)
            {
                var replacements = Replacements(target.SourceDirectory, selectedFiles);
                foreach (var outputDirectory in target.OutputDirectories)
                {
                    if (!Directory.Exists(outputDirectory))
                    {
                        result.Problems.Add(string.Format("[{0}] Не удалось проверить: нет доступа к {1}", target.Name, outputDirectory));
                        continue;
                    }

                    List<CompatibilityProblem> problems;
                    try
                    {
                        problems = CompatibilityChecker.Check(outputDirectory, replacements);
                    }
                    catch (Exception ex)
                    {
                        result.Problems.Add(string.Format("[{0}] Не удалось проверить {1}: {2}", target.Name, outputDirectory, ex.Message));
                        continue;
                    }

                    if (problems.Count == 0)
                    {
                        continue;
                    }

                    foreach (var problem in problems)
                    {
                        result.Problems.Add(string.Format("[{0}] {1}", target.Name, problem));
                        if (problem.TargetFromBuild && problem.Target != null)
                        {
                            result.BrokenSelected.Add(problem.Target);
                        }
                    }

                    SuggestReleaseTogether(target.SourceDirectory, outputDirectory, replacements, problems, result.ReleaseTogether);
                }
            }

            return result;
        }

        /// <summary>
        /// Сломавшиеся модули из actual пробуем взять из build: если с ними проблем у них не остаётся — советуем выпустить вместе.
        /// </summary>
        private static void SuggestReleaseTogether(string sourceDirectory, string outputDirectory,
            Dictionary<string, string> replacements, List<CompatibilityProblem> problems, SortedSet<string> releaseTogether)
        {
            var candidates = problems
                .Where(problem => !problem.ModuleFromBuild && File.Exists(Path.Combine(sourceDirectory, problem.Module + ".dll")))
                .Select(problem => problem.Module)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            var extended = new Dictionary<string, string>(replacements, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                extended[candidate] = Path.Combine(sourceDirectory, candidate + ".dll");
            }

            List<CompatibilityProblem> remaining;
            try
            {
                remaining = CompatibilityChecker.Check(outputDirectory, extended);
            }
            catch (Exception)
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                if (!remaining.Any(problem => string.Equals(problem.Module, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    releaseTogether.Add(candidate + ".dll");
                }
            }
        }

        public static Dictionary<string, string> Replacements(string sourceDirectory, IEnumerable<string> fileNames)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fileName in fileNames)
            {
                var sourcePath = Path.Combine(sourceDirectory, fileName);
                if (File.Exists(sourcePath))
                {
                    replacements[Path.GetFileNameWithoutExtension(fileName)] = sourcePath;
                }
            }

            return replacements;
        }
    }
}
