using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TNovReleaser.Services
{
    /// <summary>Нарушение совместимости: сборка Module ссылается на тип/член, которого нет в сборке Target.</summary>
    internal sealed class CompatibilityProblem
    {
        public string Module { get; set; }
        public bool ModuleFromBuild { get; set; }
        public string Target { get; set; }
        public bool TargetFromBuild { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            if (Target == null)
            {
                return string.Format("{0}{1}: {2}", Module, Origin(ModuleFromBuild), Message);
            }

            return string.Format("{0}{1} → {2}{3}: {4}", Module, Origin(ModuleFromBuild), Target, Origin(TargetFromBuild), Message);
        }

        private static string Origin(bool fromBuild)
        {
            return fromBuild ? " (build)" : " (actual)";
        }
    }

    /// <summary>
    /// Проверка бинарной совместимости TNov*-сборок по метаданным (без загрузки сборок, net48 и net10 одинаково).
    /// Набор = сборки каталога actual, где выпускаемые заменены версиями из build. Для КАЖДОЙ сборки набора
    /// проверяются ссылки на типы (TypeRef) и члены (MemberRef: методы, свойства, поля) других TNov-сборок набора.
    /// Пример: TNovUtilsAR 0925 в actual вызывает LevelNumberSettings.set_maxOffset(int), а в выпускаемом
    /// TNovCommon этого свойства нет — в Revit MissingMethodException.
    /// </summary>
    internal static class CompatibilityChecker
    {
        /// <param name="actualDirectory">каталог, куда выпускаем (actual)</param>
        /// <param name="replacements">выпускаемые сборки: имя без расширения → путь в build</param>
        public static List<CompatibilityProblem> Check(string actualDirectory, IDictionary<string, string> replacements)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(actualDirectory))
            {
                foreach (var path in Directory.GetFiles(actualDirectory, AppConfig.FileNamePrefix + "*.dll"))
                {
                    paths[Path.GetFileNameWithoutExtension(path)] = path;
                }
            }

            var fromBuild = new HashSet<string>(replacements.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in replacements)
            {
                paths[pair.Key] = pair.Value;
            }

            var problems = new List<CompatibilityProblem>();
            var assemblies = new Dictionary<string, AssemblyIndex>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in paths)
            {
                try
                {
                    var index = AssemblyIndex.Open(pair.Key, pair.Value, fromBuild.Contains(pair.Key));
                    if (index != null)
                    {
                        assemblies[index.Name] = index;
                    }
                }
                catch (Exception ex)
                {
                    problems.Add(new CompatibilityProblem
                    {
                        Module = pair.Key,
                        ModuleFromBuild = fromBuild.Contains(pair.Key),
                        Message = "не удалось прочитать — " + ex.Message
                    });
                }
            }

            foreach (var module in assemblies.Values)
            {
                new ModuleChecker(module, assemblies, problems).Run();
            }

            return problems;
        }
    }

    /// <summary>Сборка и её типы по полному имени (вложенные — через '/').</summary>
    internal sealed class AssemblyIndex
    {
        public string Name { get; private set; }
        public bool FromBuild { get; private set; }
        public MetadataReader Reader { get; private set; }
        public Dictionary<string, TypeDefinitionHandle> Types { get; private set; }
        public HashSet<string> ExportedTypes { get; private set; }

        // PEReader держит память с метаданными, пока жив индекс.
        public PEReader PEReader { get; private set; }

        public static AssemblyIndex Open(string name, string path, bool fromBuild)
        {
            // Читаем в память: файл в сетевой папке не блокируется.
            var peReader = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
            if (!peReader.HasMetadata)
            {
                return null;
            }

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return null;
            }

            var index = new AssemblyIndex
            {
                Name = reader.GetString(reader.GetAssemblyDefinition().Name),
                FromBuild = fromBuild,
                Reader = reader,
                Types = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal),
                ExportedTypes = new HashSet<string>(StringComparer.Ordinal),
                PEReader = peReader
            };

            foreach (var handle in reader.TypeDefinitions)
            {
                index.Types[TypeName(reader, handle)] = handle;
            }

            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);
                var fullName = Join(reader.GetString(exported.Namespace), reader.GetString(exported.Name));
                if (exported.Implementation.Kind == HandleKind.ExportedType)
                {
                    var parent = reader.GetExportedType((ExportedTypeHandle)exported.Implementation);
                    fullName = Join(reader.GetString(parent.Namespace), reader.GetString(parent.Name)) + "/" + reader.GetString(exported.Name);
                }

                index.ExportedTypes.Add(fullName);
            }

            return index;
        }

        public static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
            {
                return TypeName(reader, declaring) + "/" + reader.GetString(type.Name);
            }

            return Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public static string Join(string ns, string name)
        {
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
    }

    /// <summary>Проверяет ссылки одной сборки на остальные сборки набора.</summary>
    internal sealed class ModuleChecker
    {
        private readonly AssemblyIndex _module;
        private readonly Dictionary<string, AssemblyIndex> _assemblies;
        private readonly List<CompatibilityProblem> _problems;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        public ModuleChecker(AssemblyIndex module, Dictionary<string, AssemblyIndex> assemblies, List<CompatibilityProblem> problems)
        {
            _module = module;
            _assemblies = assemblies;
            _problems = problems;
        }

        public void Run()
        {
            var reader = _module.Reader;

            foreach (var handle in reader.TypeReferences)
            {
                string assemblyName;
                string typeName;
                if (!TryResolveTypeReference(reader, handle, out assemblyName, out typeName))
                {
                    continue;
                }

                AssemblyIndex target;
                if (!IsTNov(assemblyName))
                {
                    continue;
                }

                if (!_assemblies.TryGetValue(assemblyName, out target))
                {
                    Report(assemblyName, false, "нет сборки " + assemblyName + ".dll");
                    continue;
                }

                if (!target.Types.ContainsKey(typeName) && !target.ExportedTypes.Contains(typeName))
                {
                    Report(target.Name, target.FromBuild, "нет типа " + typeName);
                }
            }

            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                var typeHandle = ParentTypeReference(reader, member.Parent);
                if (typeHandle.IsNil)
                {
                    continue;
                }

                string assemblyName;
                string typeName;
                AssemblyIndex target;
                if (!TryResolveTypeReference(reader, typeHandle, out assemblyName, out typeName) ||
                    !IsTNov(assemblyName) ||
                    !_assemblies.TryGetValue(assemblyName, out target))
                {
                    continue;
                }

                TypeDefinitionHandle typeDef;
                if (!target.Types.TryGetValue(typeName, out typeDef))
                {
                    continue; // отсутствие типа уже сообщено выше (или тип переадресован)
                }

                var name = reader.GetString(member.Name);
                var signature = Signature(reader, member);
                if (signature == null)
                {
                    continue;
                }

                if (!HasMember(target, typeDef, name, member.GetKind(), signature, 0))
                {
                    Report(target.Name, target.FromBuild, string.Format("нет {0} {1}::{2}", member.GetKind() == MemberReferenceKind.Field ? "поля" : "метода", typeName, name + signature));
                }
            }
        }

        private void Report(string target, bool targetFromBuild, string message)
        {
            if (!_reported.Add(target + "|" + message))
            {
                return;
            }

            _problems.Add(new CompatibilityProblem
            {
                Module = _module.Name,
                ModuleFromBuild = _module.FromBuild,
                Target = target,
                TargetFromBuild = targetFromBuild,
                Message = message
            });
        }

        /// <summary>Ищет член в типе и его базовых типах, пока они в TNov-сборках набора. Не удалось разобрать базовый тип — считаем найденным.</summary>
        private bool HasMember(AssemblyIndex assembly, TypeDefinitionHandle typeHandle, string name, MemberReferenceKind kind, string signature, int depth)
        {
            if (depth > 32)
            {
                return true;
            }

            var reader = assembly.Reader;
            var type = reader.GetTypeDefinition(typeHandle);

            if (kind == MemberReferenceKind.Method)
            {
                foreach (var handle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(handle);
                    if (reader.StringComparer.Equals(method.Name, name) &&
                        FormatMethod(method.DecodeSignature(new SignatureNameProvider(reader), null)) == signature)
                    {
                        return true;
                    }
                }
            }
            else
            {
                foreach (var handle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(handle);
                    if (reader.StringComparer.Equals(field.Name, name) &&
                        " : " + field.DecodeSignature(new SignatureNameProvider(reader), null) == signature)
                    {
                        return true;
                    }
                }
            }

            // Методы интерфейсов вызываются через сам интерфейс, базовый тип у интерфейса пустой.
            var baseType = type.BaseType;
            if (baseType.IsNil)
            {
                return false;
            }

            if (baseType.Kind == HandleKind.TypeSpecification)
            {
                baseType = GenericTypeOf(reader, (TypeSpecificationHandle)baseType);
                if (baseType.IsNil)
                {
                    return true;
                }
            }

            if (baseType.Kind == HandleKind.TypeDefinition)
            {
                return HasMember(assembly, (TypeDefinitionHandle)baseType, name, kind, signature, depth + 1);
            }

            string baseAssembly;
            string baseName;
            AssemblyIndex target;
            TypeDefinitionHandle baseDef;
            if (!TryResolveTypeReference(reader, (TypeReferenceHandle)baseType, out baseAssembly, out baseName))
            {
                return true;
            }

            if (string.Equals(baseAssembly, assembly.Name, StringComparison.OrdinalIgnoreCase))
            {
                target = assembly;
            }
            else if (!IsTNov(baseAssembly) || !_assemblies.TryGetValue(baseAssembly, out target))
            {
                // Компилятор пишет в MemberRef тип, где член объявлен: член Window/Object имел бы родителем их, а не TNov-тип.
                return false;
            }

            if (!target.Types.TryGetValue(baseName, out baseDef))
            {
                return true;
            }

            return HasMember(target, baseDef, name, kind, signature, depth + 1);
        }

        private static string Signature(MetadataReader reader, MemberReference member)
        {
            var provider = new SignatureNameProvider(reader);
            switch (member.GetKind())
            {
                case MemberReferenceKind.Method:
                    return FormatMethod(member.DecodeMethodSignature(provider, null));
                case MemberReferenceKind.Field:
                    return " : " + member.DecodeFieldSignature(provider, null);
                default:
                    return null;
            }
        }

        private static string FormatMethod(MethodSignature<string> signature)
        {
            var generic = signature.GenericParameterCount > 0 ? "``" + signature.GenericParameterCount : string.Empty;
            var instance = signature.Header.IsInstance ? string.Empty : "static ";
            return string.Format("{0}({1}) : {2}{3}", generic, string.Join(", ", signature.ParameterTypes), instance, signature.ReturnType);
        }

        /// <summary>Тип-владелец MemberRef: TypeRef напрямую или generic-инстанциация (TypeSpec) над TypeRef.</summary>
        private static TypeReferenceHandle ParentTypeReference(MetadataReader reader, EntityHandle parent)
        {
            if (parent.Kind == HandleKind.TypeReference)
            {
                return (TypeReferenceHandle)parent;
            }

            if (parent.Kind == HandleKind.TypeSpecification)
            {
                var generic = GenericTypeOf(reader, (TypeSpecificationHandle)parent);
                if (!generic.IsNil && generic.Kind == HandleKind.TypeReference)
                {
                    return (TypeReferenceHandle)generic;
                }
            }

            return default(TypeReferenceHandle);
        }

        /// <summary>Для TypeSpec вида GenericInst&lt;T&gt; — handle самого generic-типа, иначе nil.</summary>
        private static EntityHandle GenericTypeOf(MetadataReader reader, TypeSpecificationHandle handle)
        {
            var blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
            {
                return default(EntityHandle);
            }

            blob.ReadCompressedInteger(); // CLASS / VALUETYPE
            return blob.ReadTypeHandle();
        }

        /// <summary>Сборка и полное имя типа по TypeRef. false — ссылка внутрь своей сборки или модуля.</summary>
        private static bool TryResolveTypeReference(MetadataReader reader, TypeReferenceHandle handle, out string assemblyName, out string typeName)
        {
            var type = reader.GetTypeReference(handle);
            var scope = type.ResolutionScope;
            var name = AssemblyIndex.Join(reader.GetString(type.Namespace), reader.GetString(type.Name));

            if (scope.Kind == HandleKind.TypeReference)
            {
                string parentName;
                if (!TryResolveTypeReference(reader, (TypeReferenceHandle)scope, out assemblyName, out parentName))
                {
                    typeName = null;
                    return false;
                }

                typeName = parentName + "/" + reader.GetString(type.Name);
                return true;
            }

            if (scope.Kind == HandleKind.AssemblyReference)
            {
                assemblyName = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                typeName = name;
                return true;
            }

            assemblyName = null;
            typeName = null;
            return false;
        }

        private static bool IsTNov(string assemblyName)
        {
            return assemblyName != null && assemblyName.StartsWith(AppConfig.FileNamePrefix, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Типы сигнатуры строкой без имени сборки: одинаково для ссылки и для определения в другой сборке.</summary>
    internal sealed class SignatureNameProvider : ISignatureTypeProvider<string, object>
    {
        private readonly MetadataReader _reader;

        public SignatureNameProvider(MetadataReader reader)
        {
            _reader = reader;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) { return typeCode.ToString(); }
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) { return AssemblyIndex.TypeName(reader, handle); }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var name = AssemblyIndex.Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "/" + reader.GetString(type.Name);
            }

            return name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        public string GetSZArrayType(string elementType) { return elementType + "[]"; }
        public string GetArrayType(string elementType, ArrayShape shape) { return elementType + "[" + new string(',', shape.Rank - 1) + "]"; }
        public string GetByReferenceType(string elementType) { return elementType + "&"; }
        public string GetPointerType(string elementType) { return elementType + "*"; }
        public string GetPinnedType(string elementType) { return elementType; }
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) { return genericType + "<" + string.Join(", ", typeArguments) + ">"; }
        public string GetGenericTypeParameter(object genericContext, int index) { return "!" + index; }
        public string GetGenericMethodParameter(object genericContext, int index) { return "!!" + index; }
        public string GetFunctionPointerType(MethodSignature<string> signature) { return "fnptr"; }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            // modreq (например IsExternalInit у init-сеттера) — часть сигнатуры, modopt — нет.
            return isRequired ? unmodifiedType + " modreq(" + modifier + ")" : unmodifiedType;
        }
    }
}
