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

                    var fieldType = Utils.Il2CppTypeToCppType(field.FieldType);

                    WriteIndented($"{(field.IsStatic ? "static " : "")}{fieldType} {Utils.FormatInvalidName(fieldName)};");
                }

                Console.WriteLine("[+] Class is a struct! Making normal setup");
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

                var fieldType = Utils.Il2CppTypeToCppType(field.FieldType);

                //get
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

                var methodType = Utils.Il2CppTypeToCppType(method.ReturnType);

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
                        var paramType = Utils.Il2CppTypeToCppType(param.Type);

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


                WriteIndented(string.Format("template <typename T = {0}>", methodType), true);
                currentFile.WriteLine(string.Format(" {0}{1} {2}({3}) {{", (method.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(methodName), string.Join(", ", methodParams)));
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

            indentLevel = 0;

            WriteIndented("namespace " + clazz.Module.Assembly.Name.Replace(".dll", "").Replace(".", "").Replace("-", "_") + " {");
            indentLevel++;

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

            for (int i = 0; i < indentLevel; i++)
            {
                indentLevel--;
                WriteIndented("}");
            }

            indentLevel = 0;
            WriteIndented("}");
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
                outputPath += "/" + module.Name;

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

            string moduleOutput = OUTPUT_DIR + "/" + currentModule.Name;

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

            if (Directory.Exists(args[0]))
            {
                foreach(var file in Directory.GetFiles(args[0]))
                {
                    ParseModule(file);
                }
            }
            else
            {
                ParseModule(args[0]);
            }
        }
    }
}
;