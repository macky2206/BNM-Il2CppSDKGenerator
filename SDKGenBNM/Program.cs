using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using dnlib.DotNet;
using dnlib.IO;
using dnlib.Utils;
using System.Text.RegularExpressions;
using System.Collections;

namespace Il2CppSDK
{
    class Program
    {
        static Dictionary<string, int> m_DuplicateMethodTable = new Dictionary<string, int>();
        static string OUTPUT_DIR = "SDK";
        static ModuleDefMD currentModule = null;
        static StreamWriter currentFile = null;
        static int indentLevel = 0;

        static void ParseFields(TypeDef clazz)
        {
            if (clazz.IsStruct())
            {
                foreach (var rid in currentModule.Metadata.GetFieldRidList(clazz.Rid))
                {
                    var field = currentModule.ResolveField(rid);

                    if (field == null)
                    {
                        continue;
                    }

                    var fieldName = field.Name.Replace("::", "_").Replace("<", "$").Replace(">", "$").Replace("k__BackingField", "").Replace(".", "_").Replace("`", "_");

                    if (fieldName.Equals("auto") || fieldName.Equals("register"))
                        fieldName += "_";

                    var fieldType = Utils.Il2CppTypeToCppType(field.FieldSig.GetFieldType(), clazz);

                    WriteIndented($"{(field.IsStatic ? "static " : "")}{fieldType} {Utils.FormatInvalidName(fieldName)};");
                }
                return;
            }

            foreach (var rid in currentModule.Metadata.GetFieldRidList(clazz.Rid))
            {
                var field = currentModule.ResolveField(rid);

                if (field == null)
                {
                    continue;
                }

                var fieldName = field.Name.Replace("::", "_").Replace("<", "$").Replace(">", "$").Replace("k__BackingField", "").Replace(".", "_").Replace("`", "_");

                if (fieldName.Equals("auto") || fieldName.Equals("register"))
                    fieldName += "_";

                var fieldType = Utils.Il2CppTypeToCppType(field.FieldType, clazz);

                //get

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", field.FieldType.FullName));
                WriteIndented(string.Format("template <typename T = {0}>", fieldType), true);
                currentFile.WriteLine(string.Format(" {0}{1} {2}() {{", (field.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(fieldName)));
                if (field.IsStatic)
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", "T", fieldName));
                    WriteIndented("\treturn __bnm__field__();");
                }
                else
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", "T", fieldName));
                    WriteIndented("\t__bnm__field__.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    WriteIndented("\treturn __bnm__field__();");
                }
                WriteIndented("}");

                // set
                WriteIndented(string.Format("/* @param {0} Orig Type: {1} */", "value", field.FieldType.FullName));
                WriteIndented(string.Format("{0}{1} set_{2}({3}) {{", (field.IsStatic ? "static " : ""), "void", Utils.FormatInvalidName(fieldName), fieldType + " value"));
                if (field.IsStatic)
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", fieldType, fieldName));
                    WriteIndented("\t__bnm__field__.Set(value);");
                }
                else
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", fieldType, fieldName));
                    WriteIndented("\t__bnm__field__.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    WriteIndented("\t__bnm__field__.Set(value);");
                }
                WriteIndented("}");
            }
        }
        
        static void ParseMethods(TypeDef clazz)
        {
            if (clazz.IsStruct())
            {
                return;
            }

            foreach (var rid in currentModule.Metadata.GetMethodRidList(clazz.Rid))
            {
                var method = currentModule.ResolveMethod(rid);

                if (method == null || method.IsConstructor || method.IsStaticConstructor)
                {
                    continue;
                }

                var methodName = method.Name.Replace("::", "_").Replace("<", "").Replace(">", "").Replace(".", "_").Replace("`", "_");

                if (methodName.Equals("auto") || methodName.Equals("register"))
                    methodName += "_";

                var methodType = Utils.Il2CppTypeToCppType(method.ReturnType, clazz);

                string methodKey = clazz.Namespace + clazz.FullName + method.Name;

                if (m_DuplicateMethodTable.ContainsKey(methodKey))
                {
                    methodName += "_" + m_DuplicateMethodTable[methodKey]++;
                }
                else
                {
                    m_DuplicateMethodTable.Add(methodKey, 1);
                }

                List<string> methodParams = new List<string>();
                List<string> paramTypes = new List<string>();
                List<string> paramNames = new List<string>();

                foreach (var param in method.Parameters)
                {
                    if (param.IsNormalMethodParameter)
                    {
                        var paramTypeDef = param.Type.ToTypeDefOrRef().ResolveTypeDef();
                        var paramType = Utils.Il2CppTypeToCppType(param.Type, clazz);

                        if (paramTypeDef != null && paramTypeDef.IsEnum)
                        {
                            paramType = Utils.GetEnumType(paramTypeDef);
                        }
                        
                        if (paramTypeDef != null && paramTypeDef.TryGetGenericInstSig() != null)
                        {
                            paramType = "void*";
                        }

                        if (param.HasParamDef && param.ParamDef.IsOut)
                            paramType += "*";

                        var originalName = param.Name;
                        if (originalName == "auto" || originalName == "register")
                            originalName += "_";

                        paramTypes.Add(paramType);
                        paramNames.Add(originalName);
                    }
                }

                paramNames = Utils.MakeValidParams(paramNames.ToArray()).ToList();

                for (int i = 0; i < paramNames.Count; i++)
                {
                    methodParams.Add(paramTypes[i] + " " + Utils.FormatInvalidName(paramNames[i]));
                }

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", method.ReturnType.FullName));
                WriteIndented(string.Format("template <typename T = {0}>", methodType), true);
                currentFile.WriteLine(string.Format(" {0}{1} {2}({3}) {{",
                    method.IsStatic ? "static " : "", "T", Utils.FormatInvalidName(methodName), string.Join(", ", methodParams)));

                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 || (method.ImplAttributes & MethodImplAttributes.InternalCall) != 0)
                {
                    string paramList = string.Join(", ", paramTypes);
                    string paramCall = string.Join(", ", paramNames.Select(Utils.FormatInvalidName));

                    if (!method.IsStatic)
                    {
                        if (!string.IsNullOrWhiteSpace(paramList))
                            paramList = "BNM::IL2CPP::Il2CppObject*, " + paramList;
                        else
                            paramList = "BNM::IL2CPP::Il2CppObject*";

                        if (!string.IsNullOrWhiteSpace(paramCall))
                            paramCall = "(BNM::IL2CPP::Il2CppObject*)this, " + paramCall;
                        else
                            paramCall = "(BNM::IL2CPP::Il2CppObject*)this";
                    }

                    WriteIndented(string.Format("\tstatic auto __bnm__method__ = ({0}(*)({1}))BNM::GetExternMethod(\"{2}::{3}::{4}\");",
                        "T", paramList, clazz.Namespace, clazz.Name, method.Name));

                    WriteIndented(string.Format("\treturn (T)__bnm__method__({0});", paramCall));
                }
                else
                {
                    if (!method.IsStatic)
                    {
                        WriteIndented(string.Format("\tstatic BNM::Method<T> __bnm__method__ = StaticClass().GetMethod(\"{0}\", {1});", method.Name, methodParams.Count));

                        WriteIndented("\treturn __bnm__method__[(BNM::IL2CPP::Il2CppObject*)this](", true);
                        currentFile.Write(string.Join(", ", paramNames.Select(x => Utils.FormatInvalidName(x))));
                        currentFile.WriteLine(");");
                    }
                    else
                    {
                        WriteIndented(string.Format("\tstatic BNM::Method<T> __bnm__method__ = StaticClass().GetMethod(\"{0}\", {1});", method.Name, methodParams.Count));

                        WriteIndented("\treturn __bnm__method__(", true);
                        currentFile.Write(string.Join(", ", paramNames.Select(x => Utils.FormatInvalidName(x))));
                        currentFile.WriteLine(");");
                    }
                }

                
                WriteIndented("}");
            }
        }
        
        static void WriteIndented(string line, bool isWrite = false)
        {
            if (isWrite)
                currentFile.Write(new string('\t', indentLevel) + line);
            else
                currentFile.WriteLine(new string('\t', indentLevel) + line);
        }
        static void ParseClass(TypeDef clazz)
        {
            var module = clazz.Module;
            var namespaze = clazz.Namespace;
            var className = (string)clazz.Name;
            var classFilename = string.Concat(className.Split(Path.GetInvalidFileNameChars()));
            var validClassname = Utils.FormatInvalidName(className);

            currentFile.WriteLine("#pragma once");
            currentFile.WriteLine("#include <BNMIncludes.hpp>");
            currentFile.WriteLine();

            if (clazz.IsEnum)
            {
                var enumFields = clazz.Fields
                    .Where(f => f.IsLiteral && f.Constant?.Value != null && f.IsStatic)
                    .ToList();

                for (int i = 0; i < enumFields.Count; i++)
                {
                    currentFile.WriteLine("#undef " + Utils.FormatInvalidName(enumFields[i].Name));
                }
            }

            indentLevel = 0;

            string[] nameSpaceSplit = namespaze.ToString().Split('.');
            if (nameSpaceSplit.Length == 0 || (nameSpaceSplit.Length == 1 && nameSpaceSplit[0] == ""))
            {
                WriteIndented("namespace GlobalNamespace {");
                indentLevel++;
            }
            else
            {
                foreach (var part in nameSpaceSplit)
                {
                    WriteIndented("namespace " + part + " {");
                    indentLevel++;
                }
            }

            if (clazz.IsEnum)
            {
                string type = Utils.GetEnumType(clazz);

                WriteIndented($"enum class {validClassname} : {type}");
                WriteIndented("{");
                indentLevel++;

                var enumFields = clazz.Fields
                    .Where(f => f.IsLiteral && f.Constant?.Value != null && f.IsStatic)
                    .ToList();

                for (int i = 0; i < enumFields.Count; i++)
                {
                    var field = enumFields[i];
                    var comma = i == enumFields.Count - 1 ? "" : ",";
                    WriteIndented($"{Utils.FormatInvalidName(field.Name)} = {field.Constant.Value}{comma}");
                }

                indentLevel--;
                WriteIndented("};");

                while (indentLevel > 0)
                {
                    indentLevel--;
                    WriteIndented("}");
                }

                return;
            }

            if (clazz.IsSealed && clazz.IsAbstract)
            {
                WriteIndented("class " + validClassname);
                currentFile.WriteLine();
                WriteIndented("{");
                indentLevel++;
                WriteIndented("public:");
            }
            else
            {
                WriteIndented((clazz.IsStruct() ? "struct " : "class ") + validClassname, true);

                if (!clazz.IsStruct())
                {
                    if (clazz.BaseType != null)
                    {
                        if (clazz.BaseType.FullName == "UnityEngine.MonoBehaviour")
                        {
                            currentFile.WriteLine(" : public BNM::UnityEngine::MonoBehaviour");
                        }
                        else
                        {
                            currentFile.WriteLine(" : public BNM::IL2CPP::Il2CppObject");
                        }
                    }
                    else
                    {
                        currentFile.WriteLine();
                    }
                }
                else
                {
                    currentFile.WriteLine();
                }

                WriteIndented("{");
                indentLevel++;

                if (!clazz.IsStruct()) WriteIndented("public:");
            }

            WriteIndented("static BNM::Class StaticClass() {");
            WriteIndented(string.Format("\treturn BNM::Class(\"{0}\", \"{1}\", BNM::Image(\"{2}\"));", namespaze, className, module.Name));
            WriteIndented("}");
            WriteIndented("");

            ParseFields(clazz);
            WriteIndented("");
            ParseMethods(clazz);
            WriteIndented("");

            indentLevel--;
            WriteIndented("};");

            while (indentLevel > 0)
            {
                indentLevel--;
                WriteIndented("}");
            }
        }


        static void ParseClasses()
        {
            if (currentModule == null)
                return;

            foreach(var rid in currentModule.Metadata.GetTypeDefRidList())
            {
                var type = currentModule.ResolveTypeDef(rid);

                if (type == null)
                    continue;

                var module = type.Module;
                var namespaze = type.Namespace.Replace("<", "").Replace(">", "");
                var className = (string)type.Name.Replace("<", "").Replace(">", "");
                var classFilename = string.Concat(className.Split(Path.GetInvalidFileNameChars()));
                var validClassname = Utils.FormatInvalidName(className);

                string outputPath = OUTPUT_DIR;

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                if (namespaze.Length > 0)
                {
                    File.AppendAllText(outputPath + "/" + namespaze + ".h", string.Format("#include \"Includes/{0}/{1}.h\"\r\n", namespaze, classFilename));
                }
                else
                {
                    File.AppendAllText(outputPath + "/-.h", string.Format("#include \"Includes/{0}.h\"\r\n", classFilename));
                }

                outputPath += "/Includes";

                if(namespaze.Length > 0)
                {
                    outputPath += "/" + namespaze;
                }

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                outputPath += "/" + classFilename + ".h";

                currentFile = new StreamWriter(outputPath);

                ParseClass(type);
                currentFile.Close();
            }
        }
        
        static void ParseModule(string moduleFile)
        {
            Console.WriteLine("Generating SDK for {0}...", Path.GetFileName(moduleFile));

            ModuleContext modCtx = ModuleDef.CreateModuleContext();
            currentModule = ModuleDefMD.Load(moduleFile, modCtx);

            string moduleOutput = OUTPUT_DIR;

            if (!Directory.Exists(moduleOutput))
                Directory.CreateDirectory(moduleOutput);

            ParseClasses();
        }
        
        static void Main(string[] args)
        {
            if(args.Length < 1)
            {
                Console.WriteLine("Invalid Arguments!");
                return;
            }

            if (Directory.Exists(OUTPUT_DIR))
                Directory.Delete(OUTPUT_DIR, true);

            string inputPath = args[0];
            if (inputPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                ParseModule(inputPath);
            }
            else if (inputPath.EndsWith("dump.cs", StringComparison.OrdinalIgnoreCase) || inputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var namespaces = DumpCsParser.Parse(inputPath);
                GenerateSdkFromDumpCs(namespaces);
            }
            else if (Directory.Exists(inputPath))
            {
                foreach(var file in Directory.GetFiles(inputPath))
                {
                    if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        ParseModule(file);
                    else if (file.EndsWith("dump.cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        var namespaces = DumpCsParser.Parse(file);
                        GenerateSdkFromDumpCs(namespaces);
                    }
                }
            }
            else
            {
                Console.WriteLine("Unsupported file type. Please provide a .dll or dump.cs file.");
            }
        }
        // Generates SDK output from parsed dump.cs data
        static void GenerateSdkFromDumpCs(Dictionary<string, DumpCsParser.Namespace> namespaces)
        {
            string outputDir = OUTPUT_DIR;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string SanitizeFileName(string name)
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                foreach (var c in invalidChars)
                {
                    name = name.Replace(c, '_');
                }
                return name;
            }

            // Map dump.cs types to proper C++ types
            string MapDumpTypeToCpp(string dumpType)
            {
                if (string.IsNullOrEmpty(dumpType)) return "BNM::IL2CPP::Il2CppObject*";
                
                return dumpType switch
                {
                    "int" => "int",
                    "bool" => "bool",
                    "float" => "float",
                    "double" => "double",
                    "void" => "void",
                    "string" or "String" => "BNM::Structures::Mono::String*",
                    "Vector2" => "BNM::Structures::Unity::Vector2",
                    "Vector3" => "BNM::Structures::Unity::Vector3",
                    "Vector4" => "BNM::Structures::Unity::Vector4",
                    "Quaternion" => "BNM::Structures::Unity::Quaternion",
                    "Rect" => "BNM::Structures::Unity::Rect",
                    "Color" => "BNM::Structures::Unity::Color",
                    "Color32" => "BNM::Structures::Unity::Color32",
                    "GameObject" => "BNM::IL2CPP::Il2CppObject*",
                    "Transform" => "BNM::IL2CPP::Il2CppObject*",
                    "MonoBehaviour" => "BNM::UnityEngine::MonoBehaviour*",
                    "Object" => "BNM::UnityEngine::Object*",
                    var t when t.EndsWith("[]") => "BNM::Structures::Mono::Array<BNM::IL2CPP::Il2CppObject*>*",
                    _ => "BNM::IL2CPP::Il2CppObject*"
                };
            }

            // Output to SDK/Includes/ to match .dll output (OUTPUT_DIR is already "SDK")
            string sdkOutputDir = Path.Combine(outputDir, "Includes");
            if (!Directory.Exists(sdkOutputDir))
                Directory.CreateDirectory(sdkOutputDir);

            foreach (var ns in namespaces.Values)
            {
                foreach (var clazz in ns.Classes.Values)
                {
                    string classFile = Path.Combine(sdkOutputDir, SanitizeFileName(clazz.Name) + ".h");
                    using (var sw = new StreamWriter(classFile))
                    {
                        // Match .dll output template exactly
                        sw.WriteLine("#pragma once");
                        sw.WriteLine("#include <BNMIncludes.hpp>");
                        sw.WriteLine();
                        sw.WriteLine("namespace GlobalNamespace {");
                        sw.WriteLine($"\tclass {clazz.Name} : public BNM::UnityEngine::MonoBehaviour");
                        sw.WriteLine("\t{");
                        sw.WriteLine("\t\tpublic:");
                        sw.WriteLine("\t\tstatic BNM::Class StaticClass() {…}");
                        sw.WriteLine();
                        
                        // Generate field setters
                        foreach (var field in clazz.Fields)
                        {
                            // Use proper C++ type mapping
                            string cppType = MapDumpTypeToCpp(field.Type);
                            sw.WriteLine($"\t\t/* @brief Orig Type: {field.Type} */");
                            sw.WriteLine($"\t\ttemplate <typename T = {cppType}>");
                            sw.WriteLine($"\t\t/* @param value Orig Type: {field.Type} */");
                            sw.WriteLine($"\t\tvoid set_{field.Name}({cppType} value) {{…}}");
                        }
                        
                        // Generate method signatures
                        foreach (var method in clazz.Methods)
                        {
                            string returnType = MapDumpTypeToCpp(method.Type);
                            string paramList = string.Join(", ", method.Params);
                            sw.WriteLine($"\t\t/* @brief Orig Type: {method.Type} */");
                            sw.WriteLine($"\t\ttemplate <typename T = {returnType}>");
                            sw.WriteLine($"\t\t{returnType} {method.Name}({paramList}) {{…}}");
                        }
                        sw.WriteLine("\t};");
                        sw.WriteLine("}");
                    }
                }
            }
            Console.WriteLine($"SDK generated from dump.cs in '{outputDir}'");
        }

        // Dump.cs parser mimicking DumpSDK Python logic
        public static class DumpCsParser
        {
            public class Namespace
            {
                public string Name = string.Empty;
                public Dictionary<string, Class> Classes = new();
                public Dictionary<string, Struct> Structs = new();
                public Dictionary<string, Enum> Enums = new();
            }
            public class Class
            {
                public string Name = string.Empty;
                public List<Field> Fields = new();
                public List<Method> Methods = new();
            }
            public class Struct : Class { }
            public class Enum
            {
                public string Name = string.Empty;
                public List<Field> Fields = new();
            }
            public class Field
            {
                public string Name = string.Empty;
                public string? Type;
                public string? Offset;
            }
            public class Method
            {
                public string Name = string.Empty;
                public string Type = string.Empty;
                public string? Offset;
                public List<string> Modifiers = new();
                public List<string> Params = new();
            }

            public static Dictionary<string, Namespace> Parse(string path)
            {
                var namespaces = new Dictionary<string, Namespace>();
                Namespace? currentNamespace = null;
                Class? currentClass = null;
                Struct? currentStruct = null;
                Enum? currentEnum = null;
                string? lastOffset = null;
                foreach (var line in File.ReadLines(path))
                {
                    var l = line.Trim();
                    if (string.IsNullOrEmpty(l)) continue;
                    // Namespace
                    if (l.Contains("// Namespace: "))
                    {
                        var name = l.Split("Namespace: ")[1].Trim();
                        if (string.IsNullOrEmpty(name)) name = "NO_NAME_SPACE";
                        if (!namespaces.ContainsKey(name)) namespaces[name] = new Namespace { Name = name };
                        currentNamespace = namespaces[name];
                    }
                    // Class
                    else if (l.Contains(" class "))
                    {
                        var name = Regex.Match(l, @"class ([^:/{]+)").Groups[1].Value.Trim();
                        if (currentNamespace == null) {
                            if (!namespaces.ContainsKey("DefaultNamespace")) namespaces["DefaultNamespace"] = new Namespace { Name = "DefaultNamespace" };
                            currentNamespace = namespaces["DefaultNamespace"];
                        }
                        currentClass = new Class { Name = name };
                        currentNamespace.Classes[name] = currentClass;
                    }
                    // Struct
                    else if (l.Contains(" struct "))
                    {
                        var name = Regex.Match(l, @"struct ([^:/{]+)").Groups[1].Value.Trim();
                        if (currentNamespace == null) {
                            if (!namespaces.ContainsKey("DefaultNamespace")) namespaces["DefaultNamespace"] = new Namespace { Name = "DefaultNamespace" };
                            currentNamespace = namespaces["DefaultNamespace"];
                        }
                        currentStruct = new Struct { Name = name };
                        currentNamespace.Structs[name] = currentStruct;
                    }
                    // Enum
                    else if (l.Contains("enum "))
                    {
                        var name = Regex.Match(l, @"enum ([^ ]+)").Groups[1].Value.Trim();
                        if (currentNamespace == null) {
                            if (!namespaces.ContainsKey("DefaultNamespace")) namespaces["DefaultNamespace"] = new Namespace { Name = "DefaultNamespace" };
                            currentNamespace = namespaces["DefaultNamespace"];
                        }
                        currentEnum = new Enum { Name = name };
                        currentNamespace.Enums[name] = currentEnum;
                    }
                    // Offset
                    else if (l.Contains("Offset: 0x"))
                    {
                        lastOffset = l.Split("Offset: ")[1].Split(' ')[0];
                    }
                    // Method
                    else if (l.Contains(") { }"))
                    {
                        var methodMatch = Regex.Match(l, @"(public|private|protected|internal|static|virtual|override|abstract|sealed|extern|unsafe|new|readonly|volatile|partial|async|\s)+([\w<>]+) ([\w]+)\(([^)]*)\) { }");
                        if (methodMatch.Success && currentClass != null)
                        {
                            var type = methodMatch.Groups[2].Value;
                            var name = methodMatch.Groups[3].Value;
                            var paramStr = methodMatch.Groups[4].Value;
                            var paramList = paramStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            var modifiers = l.Split(type)[0].Trim().Split(' ');
                            currentClass.Methods.Add(new Method
                            {
                                Name = name,
                                Type = type,
                                Offset = lastOffset,
                                Modifiers = new List<string>(modifiers),
                                Params = new List<string>(paramList)
                            });
                        }
                    }
                    // Field
                    else if (l.Contains("; // 0x") && currentClass != null)
                    {
                        var parts = l.Split(';');
                        var left = parts[0].Trim();
                        var name = left.Split(' ')[^1];
                        var type = left.Split(' ')[^2];
                        var offset = "0x" + l.Split("; // 0x")[1].Split('\n')[0];
                        currentClass.Fields.Add(new Field { Name = name, Type = type, Offset = offset });
                    }
                    // Enum Field
                    else if ((l.Contains("public const") || l.Contains("private const")) && currentEnum != null)
                    {
                        var parts = l.Split(' ');
                        var name = parts[^2];
                        var value = parts[^1].TrimEnd(';');
                        currentEnum.Fields.Add(new Field { Name = name, Type = null, Offset = value });
                    }
                }
                return namespaces;
            }
        }
    }
}