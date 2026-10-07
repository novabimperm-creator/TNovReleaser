using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    /// <summary>
    /// Проверяет, что все типы, на которые ссылаются выпускаемые сборки, есть в TNov*-сборках, остающихся в actual.
    /// Пример: TNov.dll 1006 использует TNovUtils.Issues.ModelSync.ModelSyncService, а в actual лежит TNovUtils 928,
    /// где этого класса нет, — Revit не запускает плагин целиком (TypeLoadException в OnStartup).
    /// Версии сборок не сравниваются: каждая сборка поднимает версии всех плагинов, а Revit 2022 их не сверяет.
    /// </summary>
    internal static class DependencyCheckService
    {
        public static List<string> FindMissingTypes(IList<FileItem> files, IEnumerable<ReleaseTarget> targets)
        {
            var problems = new List<string>();
            var selectedFiles = files.Where(file => file.Selected).Select(file => file.FileName).ToArray();

            foreach (var target in targets)
            {
                foreach (var outputDirectory in target.OutputDirectories)
                {
                    // Отдельный домен на каждый каталог: сборки 2022 и 2027 с одинаковыми именами
                    // нельзя загрузить в один ReflectionOnly-контекст.
                    var domain = AppDomain.CreateDomain("TNovReleaserCheck", null, new AppDomainSetup
                    {
                        ApplicationBase = Path.GetDirectoryName(typeof(MissingTypeChecker).Assembly.Location)
                    });
                    try
                    {
                        var checker = (MissingTypeChecker)domain.CreateInstanceAndUnwrap(
                            typeof(MissingTypeChecker).Assembly.FullName,
                            typeof(MissingTypeChecker).FullName);

                        foreach (var problem in checker.Check(target.SourceDirectory, outputDirectory, selectedFiles))
                        {
                            problems.Add(string.Format("[{0}] {1}", target.Name, problem));
                        }
                    }
                    catch (Exception ex)
                    {
                        problems.Add(string.Format("[{0}] Не удалось проверить {1}: {2}", target.Name, outputDirectory, ex.Message));
                    }
                    finally
                    {
                        AppDomain.Unload(domain);
                    }
                }
            }

            return problems;
        }
    }

    /// <summary>Работает в отдельном домене: загружает сборки ReflectionOnly и резолвит каждую ссылку на тип.</summary>
    internal sealed class MissingTypeChecker : MarshalByRefObject
    {
        private const int TypeRefTable = 0x01000000;

        private string _sourceDirectory;
        private string _outputDirectory;
        private HashSet<string> _selected;

        public string[] Check(string sourceDirectory, string outputDirectory, string[] selectedFiles)
        {
            _sourceDirectory = sourceDirectory;
            _outputDirectory = outputDirectory;
            _selected = new HashSet<string>(selectedFiles.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += OnResolve;

            var problems = new List<string>();
            foreach (var fileName in selectedFiles)
            {
                var sourcePath = Path.Combine(sourceDirectory, fileName);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                Module module;
                try
                {
                    // Сборка могла уже загрузиться резолвером как зависимость другой выбранной.
                    module = LoadOnce(Path.GetFileNameWithoutExtension(fileName), sourcePath).ManifestModule;
                }
                catch (Exception ex)
                {
                    problems.Add(string.Format("{0}: не удалось прочитать — {1}", fileName, ex.Message));
                    continue;
                }

                for (var row = 1; ; row++)
                {
                    try
                    {
                        module.ResolveType(TypeRefTable | row);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        break; // строки таблицы TypeRef закончились
                    }
                    catch (TypeLoadException ex)
                    {
                        // Ссылка на сборку не из выпуска (Revit API и т.п.) не находится вовсе — это FileNotFoundException;
                        // TypeLoadException значит: сборка в actual нашлась, а типа в ней нет.
                        problems.Add(string.Format("{0}: нет типа {1} — {2}", fileName, ex.TypeName, ex.Message));
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return problems.Distinct().ToArray();
        }

        private Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (!name.StartsWith(AppConfig.FileNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Выпускаемые вместе сборки берём из build, остальные — те, что останутся в actual.
            var directory = _selected.Contains(name) ? _sourceDirectory : _outputDirectory;
            var path = Path.Combine(directory, name + ".dll");
            return File.Exists(path) ? LoadOnce(name, path) : null;
        }

        /// <summary>Сборку с тем же именем второй раз в ReflectionOnly-контекст не загрузить — берём уже загруженную.</summary>
        private static Assembly LoadOnce(string name, string path)
        {
            var loaded = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            return loaded ?? Assembly.ReflectionOnlyLoad(File.ReadAllBytes(path));
        }
    }
}
