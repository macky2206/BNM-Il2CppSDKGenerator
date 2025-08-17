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
        static string OUTPUT_CS_DIR = Path.Combine(OUTPUT_DIR, "SDKcs");
        static string OUTPUT_DLL_DIR = Path.Combine(OUTPUT_DIR, "SDKdll");
        static ModuleDefMD? currentModule = null;
        static Dictionary<string, List<DumpClass>>? parsedNamespaces = null;
        static StreamWriter? currentFile = null;
        static int indentLevel = 0;

        static void ParseFieldsDump(DumpClass clazz)
        {
            if (clazz.IsStruct)
            {
                foreach (var field in clazz.Fields)
                {
                    var fieldName = (field.Name ?? "").Replace("::", "_").Replace("<", "$").Replace(">", "$").Replace("k__BackingField", "").Replace(".", "_").Replace("`", "_");

                    if (fieldName.Equals("auto") || fieldName.Equals("register"))
                        fieldName += "_";

                    var fieldType = Utils.ConvertTypeToCpp(field.Type ?? "");

                    WriteIndented($"{(field.IsStatic ? "static " : "")}{fieldType} {Utils.FormatInvalidName(fieldName)};");
                }
                return;
            }

            foreach (var field in clazz.Fields)
            {
                var fieldName = field.Name.Replace("::", "_").Replace("<", "$" ).Replace(">", "$" ).Replace("k__BackingField", "").Replace(".", "_").Replace("`", "_");
                var originalFieldName = field.Name;

                if (fieldName.Equals("auto") || fieldName.Equals("register"))
                    fieldName += "_";

                var fieldType = Utils.ConvertTypeToCpp(field.Type ?? "");

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", field.Type));
                WriteIndented(string.Format("template <typename T = {0}>", fieldType), true);
                currentFile!.WriteLine(string.Format(" {0}{1} {2}() {{", (field.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(fieldName)));
                if (field.IsStatic)
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", "T", originalFieldName));
                    WriteIndented("\treturn __bnm__field__();");
                }
                else
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", "T", originalFieldName));
                    WriteIndented("\t__bnm__field__.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    WriteIndented("\treturn __bnm__field__();");
                }
                WriteIndented("}");

                WriteIndented(string.Format("/* @param {0} Orig Type: {1} */", "value", field.Type));
                WriteIndented(string.Format("{0}{1} set_{2}({3}) {{", (field.IsStatic ? "static " : ""), "void", Utils.FormatInvalidName(fieldName), fieldType + " value"));
                if (field.IsStatic)
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", fieldType, originalFieldName));
                    WriteIndented("\t__bnm__field__.Set(value);");
                }
                else
                {
                    WriteIndented(string.Format("\tstatic BNM::Field<{0}> __bnm__field__ = StaticClass().GetField(\"{1}\");", fieldType, originalFieldName));
                    WriteIndented("\t__bnm__field__.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    WriteIndented("\t__bnm__field__.Set(value);");
                }
                WriteIndented("}");
            }
        }

        static void ParseFields(TypeDef clazz)
        {
            if (clazz.IsStruct())
            {
                foreach (var rid in currentModule!.Metadata.GetFieldRidList(clazz.Rid))
                {
                    var field = currentModule.ResolveField(rid);

                    if (field == null)
                    {
                        continue;
                    }
                    if (field.Name.Contains("k__BackingField"))
                    {
                        continue;
                    }

                    var fieldName = field.Name.Replace("::", "_").Replace(".", "_").Replace("`", "_");

                    if (fieldName.Equals("auto") || fieldName.Equals("register"))
                        fieldName += "_";

                    var fieldType = Utils.Il2CppTypeToCppType(field.FieldSig.GetFieldType(), clazz);

                    WriteIndented($"{(field.IsStatic ? "static " : "")}{fieldType} {Utils.FormatInvalidName(fieldName)};");
                }
                return;
            }

            foreach (var rid in currentModule!.Metadata.GetFieldRidList(clazz.Rid))
            {
                var field = currentModule.ResolveField(rid);

                if (field == null)
                {
                    continue;
                }

                if (field.Name.Contains("k__BackingField"))
                {
                    continue;
                }

                var fieldName = field.Name.Replace("::", "_").Replace(".", "_").Replace("`", "_");

                if (fieldName.Equals("auto") || fieldName.Equals("register"))
                    fieldName += "_";

                var fieldType = Utils.Il2CppTypeToCppType(field.FieldType, clazz);

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", field.FieldType.FullName));
                WriteIndented(string.Format("template <typename T = {0}>", fieldType), true);
                currentFile!.WriteLine(string.Format(" {0}{1} {2}() {{", (field.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(fieldName)));
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

        static void ParseMethodsDump(DumpClass clazz)
        {
            if (clazz.IsStruct)
            {
                return;
            }

            foreach (var method in clazz.Methods)
            {
                if (method.IsConstructor)
                {
                    continue;
                }

                var methodName = (method.Name ?? "").Replace("::", "_").Replace("<", "").Replace(">", "").Replace(".", "_").Replace("`", "_");

                if (methodName.Equals("auto") || methodName.Equals("register"))
                    methodName += "_";

                var methodType = Utils.ConvertTypeToCpp(method.ReturnType ?? "");

                string methodKey = clazz.Namespace + clazz.Name + method.Name;

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
                    var paramType = Utils.ConvertTypeToCpp(param.Type ?? "");

                    if (param.IsOut)
                        paramType += "*";

                    var originalName = param.Name ?? "arg";
                    if (originalName == "auto" || originalName == "register")
                        originalName += "_";

                    paramTypes.Add(paramType);
                    paramNames.Add(originalName);
                }

                paramNames = Utils.MakeValidParams(paramNames.ToArray()).ToList();

                for (int i = 0; i < paramNames.Count; i++)
                {
                    methodParams.Add(paramTypes[i] + " " + Utils.FormatInvalidName(paramNames[i]));
                }

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", method.ReturnType ?? ""));
                WriteIndented(string.Format("template <typename T = {0}>", methodType), true);
                currentFile!.WriteLine(string.Format(" {0}{1} {2}({3}) {{",
                    method.IsStatic ? "static " : "", "T", Utils.FormatInvalidName(methodName), string.Join(", ", methodParams)));

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

                WriteIndented("}");
            }
        }

        static void ParseMethods(TypeDef clazz)
        {
            if (clazz.IsStruct())
            {
                return;
            }

            foreach (var rid in currentModule!.Metadata.GetMethodRidList(clazz.Rid))
            {
                var method = currentModule.ResolveMethod(rid);

                if (method == null || method.IsConstructor || method.IsStaticConstructor)
                {
                    continue;
                }

        var methodName = (method.Name ?? "").Replace("::", "_").Replace("<", "").Replace(">", "").Replace(".", "_").Replace("`", "_");

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

                WriteIndented(string.Format("/* @brief Orig Type: {0} */", method.ReturnType?.FullName ?? ""));
                WriteIndented(string.Format("template <typename T = {0}>", methodType), true);
                currentFile!.WriteLine(string.Format(" {0}{1} {2}({3}) {{",
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

                    WriteIndented(string.Format("\tstatic auto __bnm__method__ = ({0}(*)({1}))BNM::GetExternMethod(\"{2}.{3}::{4}\");",
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
                currentFile!.Write(new string('\t', indentLevel) + line);
            else
                currentFile!.WriteLine(new string('\t', indentLevel) + line);
        }

        static void ParseClassDump(DumpClass clazz)
        {
            var namespaze = clazz.Namespace;
            var className = clazz.Name;
            var classFilename = string.Concat((className ?? "").Split(Path.GetInvalidFileNameChars()));
            var validClassname = Utils.FormatInvalidName(className);

            currentFile!.WriteLine("#pragma once");
            currentFile.WriteLine("#include \"BNM/BNMIncludes.hpp\"");
            currentFile.WriteLine();

            if (clazz.IsEnum)
            {
                var enumFields = clazz.Fields.Where(f => f.IsLiteral && f.ConstantValue != null && f.IsStatic).ToList();

                for (int i = 0; i < enumFields.Count; i++)
                {
                    currentFile.WriteLine("#undef " + Utils.FormatInvalidName(enumFields[i].Name ?? ""));
                }
            }

            indentLevel = 0;

            string[] nameSpaceSplit = (namespaze ?? "").ToString().Split('.');
            if (nameSpaceSplit.Length == 0 || (nameSpaceSplit.Length == 1 && nameSpaceSplit[0] == "") || namespaze == "GlobalNamespace")
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
                string type = "int";

                WriteIndented($"enum class {validClassname} : {type}");
                WriteIndented("{");
                indentLevel++;

                var enumFields = clazz.Fields.Where(f => f.IsLiteral && f.ConstantValue != null && f.IsStatic).ToList();

                for (int i = 0; i < enumFields.Count; i++)
                {
                    var field = enumFields[i];
                    var comma = i == enumFields.Count - 1 ? "" : ",";
                    WriteIndented($"{Utils.FormatInvalidName(field.Name ?? "")} = {field.ConstantValue}{comma}");
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
                WriteIndented((clazz.IsStruct ? "struct " : "class ") + validClassname, true);

                if (!clazz.IsStruct)
                {
                    if (!string.IsNullOrEmpty(clazz.BaseType))
                    {
                        if (clazz.BaseType == "MonoBehaviour" || clazz.BaseType == "UnityEngine.MonoBehaviour")
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

                if (!clazz.IsStruct) WriteIndented("public:");
            }

            WriteIndented("static BNM::Class StaticClass() {");
            if (string.IsNullOrEmpty(clazz.Module))
                WriteIndented(string.Format("\treturn BNM::Class(\"{0}\", \"{1}\");", namespaze, className));
            else
                WriteIndented(string.Format("\treturn BNM::Class(\"{0}\", \"{1}\", BNM::Image(\"{2}\"));", namespaze, className, clazz.Module));
            WriteIndented("}");
            WriteIndented("");

            ParseFieldsDump(clazz);
            WriteIndented("");
            ParseMethodsDump(clazz);
            WriteIndented("");

            indentLevel--;
            WriteIndented("};");

            while (indentLevel > 0)
            {
                indentLevel--;
                WriteIndented("}");
            }
        }

        static void ParseClass(TypeDef clazz)
        {
            var module = clazz.Module;
            var namespaze = clazz.Namespace;
            var className = (string)clazz.Name;
            var classFilename = string.Concat(className.Split(Path.GetInvalidFileNameChars()));
            var validClassname = Utils.FormatInvalidName(className);

            currentFile!.WriteLine("#pragma once");
            currentFile.WriteLine("#include \"BNM/BNMIncludes.hpp\"");
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
            if (string.IsNullOrEmpty(module.Name))
                WriteIndented(string.Format("\treturn BNM::Class(\"{0}\", \"{1}\");", namespaze, className));
            else
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

        static void ParseClassesDump(string sdkOutputDir)
        {
            if (parsedNamespaces == null)
                return;

            foreach (var nk in parsedNamespaces.Keys)
            {
            }

            foreach(var namespaceKvp in parsedNamespaces)
            {
                var namespaceName = namespaceKvp.Key;
                var classes = namespaceKvp.Value;

                foreach (var clazz in classes)
                {
                    var namespaze = clazz.Namespace.Replace("<", "").Replace(">", "");
                    var className = clazz.Name.Replace("<", "").Replace(">", "");
                    var classFilename = string.Concat(className.Split(Path.GetInvalidFileNameChars()));

                    string outputPath = sdkOutputDir;

                    if (!Directory.Exists(outputPath))
                        Directory.CreateDirectory(outputPath);

                    if (namespaze.Length > 0 && namespaze != "GlobalNamespace")
                    {
                        File.AppendAllText(outputPath + "/" + namespaze + ".h", string.Format("#include \"Includes/{0}/{1}.h\"\r\n", namespaze, classFilename));
                    }
                    else
                    {
                        File.AppendAllText(outputPath + "/GlobalNamespace.h", string.Format("#include \"Includes/{0}.h\"\r\n", classFilename));
                    }

                    outputPath += "/Includes";

                    if(namespaze.Length > 0 && namespaze != "GlobalNamespace")
                    {
                        outputPath += "/" + namespaze;
                    }

                    if (!Directory.Exists(outputPath))
                        Directory.CreateDirectory(outputPath);

                    outputPath += "/" + classFilename + ".h";

                    outputPath = SanitizeFileName(outputPath);

                    try
                    {
                        currentFile = new StreamWriter(outputPath);
                        ParseClassDump(clazz);
                        currentFile.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing file: {ex.Message}");
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                }
            }
        }

        static void ParseClasses(string sdkOutputDir)
        {
            if (currentModule == null)
                return;

            foreach (var rid in currentModule.Metadata.GetTypeDefRidList())
            {
                var type = currentModule.ResolveTypeDef(rid);

                if (type == null)
                    continue;

                var module = type.Module;
                var namespaze = type.Namespace.Replace("<", "").Replace(">", "");
                var className = (string)type.Name.Replace("<", "").Replace(">", "");
                var classFilename = string.Concat(className.Split(Path.GetInvalidFileNameChars()));
                var validClassname = Utils.FormatInvalidName(className);

                string outputPath = sdkOutputDir;

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                if (namespaze.Length > 0)
                {
                    File.AppendAllText(outputPath + "/" + namespaze + ".h", string.Format("#include \"Includes/{0}/{1}.h\"\r\n", namespaze, classFilename));
                }
                else
                {
                    File.AppendAllText(outputPath + "/GlobalNamespace.h", string.Format("#include \"Includes/{0}.h\"\r\n", classFilename));
                }

                outputPath += "/Includes";

                if (namespaze.Length > 0)
                {
                    outputPath += "/" + namespaze;
                }

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                outputPath += "/" + classFilename + ".h";

                outputPath = SanitizeFileName(outputPath);
                try
                {
                    currentFile = new StreamWriter(outputPath);
                    ParseClass(type);
                    currentFile.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing file: {ex.Message}");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        private static string SanitizeFileName(string filePath)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var parts = filePath.Split('/', '\\');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new string(parts[i].Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
                if (parts[i].Length > 100)
                    parts[i] = parts[i].Substring(0, 100);
            }
            var sanitized = string.Join(Path.DirectorySeparatorChar.ToString(), parts);
            if (sanitized.Length > 255)
                sanitized = sanitized.Substring(0, 255);
            return sanitized;
        }

        static void ParseDumpFile(string dumpFile)
        {
            Console.WriteLine("Generating SDK for {0}...", Path.GetFileName(dumpFile));
            var parser = new DumpParser();
            parsedNamespaces = parser.ParseDumpFile(dumpFile);
            string moduleOutput = OUTPUT_CS_DIR;
            if (!Directory.Exists(moduleOutput))
                Directory.CreateDirectory(moduleOutput);
            ParseClassesDump(moduleOutput);
        }

        static void ParseModule(string moduleFile)
        {
            Console.WriteLine("Generating SDK for {0}...", Path.GetFileName(moduleFile));
            ModuleContext modCtx = ModuleDef.CreateModuleContext();
            currentModule = ModuleDefMD.Load(moduleFile, modCtx);
            string moduleOutput = OUTPUT_DLL_DIR;
            if (!Directory.Exists(moduleOutput))
                Directory.CreateDirectory(moduleOutput);
            ParseClasses(moduleOutput);
        }
        
        static void Main(string[] args)
        {
            if(args.Length < 1)
            {
                Console.WriteLine("Invalid Arguments!");
                Console.WriteLine("Usage: SDKGenBNM.exe <file path>");
                Console.WriteLine("Supported formats:");
                Console.WriteLine("  - .NET assemblies (.dll)");
                Console.WriteLine("  - Il2CppDumper dump.cs files");
                Console.WriteLine("  - Directory containing .NET assemblies");
                return;
            }

            if (args.Length > 0)
            {
                if (args[0].EndsWith(".cs"))
                {
                    if (Directory.Exists(OUTPUT_CS_DIR))
                        Directory.Delete(OUTPUT_CS_DIR, true);
                    Directory.CreateDirectory(OUTPUT_CS_DIR);
                }
                else if (args[0].EndsWith(".dll") || Directory.Exists(args[0]))
                {
                    if (Directory.Exists(OUTPUT_DLL_DIR))
                        Directory.Delete(OUTPUT_DLL_DIR, true);
                    Directory.CreateDirectory(OUTPUT_DLL_DIR);
                }
                if (!Directory.Exists(OUTPUT_DIR))
                    Directory.CreateDirectory(OUTPUT_DIR);
            }

            if (Directory.Exists(args[0]))
            {
                foreach(var file in Directory.GetFiles(args[0]))
                {
                    if (file.EndsWith(".dll"))
                    {
                        try
                        {
                            ParseModule(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing {file}: {ex.Message}");
                        }
                    }
                }
            }
            else if (File.Exists(args[0]))
            {
                if (args[0].EndsWith(".cs"))
                {
                    ParseDumpFile(args[0]);
                }
                else if (args[0].EndsWith(".dll"))
                {
                    ParseModule(args[0]);
                }
                else
                {
                    Console.WriteLine("Unsupported file format! Please provide a .dll or .cs file.");
                    return;
                }
            }
            else
            {
                Console.WriteLine("File or directory not found!");
                return;
            }

            if (args[0].EndsWith(".cs"))
            {
                Console.WriteLine("SDK generation for dump.cs completed!");
            }
            else if (args[0].EndsWith(".dll"))
            {
                Console.WriteLine("SDK generation completed!");
            }
        }
    }
}