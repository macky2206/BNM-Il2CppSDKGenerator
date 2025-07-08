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

        static void ParseFields(TypeDef clazz)
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

                //get
                currentFile.Write(string.Format("\ttemplate <typename T = {0}>", fieldType));
                currentFile.WriteLine(string.Format(" {0}{1} {2}() {{", (field.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(fieldName)));
                if (field.IsStatic)
                {
                    currentFile.WriteLine(string.Format("\t\tstatic BNM::Field<{0}> field = StaticClass().GetField(\"{1}\");", "T", fieldName));
                    currentFile.WriteLine("\t\treturn field();");
                }
                else
                {
                    currentFile.WriteLine(string.Format("\t\tstatic BNM::Field<{0}> field = StaticClass().GetField(\"{1}\");", "T", fieldName));
                    currentFile.WriteLine("\t\tfield.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    currentFile.WriteLine("\t\treturn field();");
                }
                currentFile.WriteLine("\t}");

                // set
                currentFile.WriteLine(string.Format("\t{0}{1} set_{2}({3}) {{", (field.IsStatic ? "static " : ""), "void", Utils.FormatInvalidName(fieldName), fieldType + " value"));
                if (field.IsStatic)
                {
                    currentFile.WriteLine(string.Format("\t\tstatic BNM::Field<{0}> field = StaticClass().GetField(\"{1}\");", fieldType, fieldName));
                    currentFile.WriteLine("\t\tfield.Set(value);");
                }
                else
                {
                    currentFile.WriteLine(string.Format("\t\tstatic BNM::Field<{0}> field = StaticClass().GetField(\"{1}\");", fieldType, fieldName));
                    currentFile.WriteLine("\t\tfield.SetInstance((BNM::IL2CPP::Il2CppObject*)this);");
                    currentFile.WriteLine("\t\tfield.Set(value);");
                }
                currentFile.WriteLine("\t}");
            }
        }
        static void ParseMethods(TypeDef clazz)
        {
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

                        if (param.HasParamDef)
                        {
                            if (param.ParamDef.IsOut)
                            {
                                paramType += "*";
                            }
                        }

                        if (param.Name.Equals("auto") || param.Name.Equals("register"))
                            param.Name += "_";

                        paramTypes.Add(paramType);
                        paramNames.Add(param.Name);

                        methodParams.Add(paramType + " " + Utils.FormatInvalidName(param.Name));
                    }
                }

                currentFile.Write(string.Format("\ttemplate <typename T = {0}>", methodType));
                currentFile.WriteLine(string.Format(" {0}{1} {2}({3}) {{", (method.IsStatic ? "static " : ""), "T", Utils.FormatInvalidName(methodName), string.Join(", ", methodParams)));
                if (!method.IsStatic)
                {
                    currentFile.WriteLine("\t\tstatic BNM::Method<T> method = StaticClass().GetMethod(\"{0}\", {1});", method.Name, methodParams.Count);

                    currentFile.Write("\t\treturn method[(BNM::IL2CPP::Il2CppObject*)this](");
                    currentFile.Write(string.Join(", ", paramNames.Select(x => Utils.FormatInvalidName(x))));
                    currentFile.WriteLine(");");
                }
                else
                {
                    currentFile.WriteLine("\t\tstatic BNM::Method<T> method = StaticClass().GetMethod(\"{0}\", {1});", method.Name, methodParams.Count);

                    currentFile.Write("\t\treturn method(");
                    currentFile.Write(string.Join(", ", paramNames.Select(x => Utils.FormatInvalidName(x))));
                    currentFile.WriteLine(");");
                }
                currentFile.WriteLine("\t}");

            }
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

            int namespaceCount = 0;

            currentFile.WriteLine("namespace " + clazz.Module.Assembly.Name.Replace(".dll", "").Replace(".", "").Replace("-", "_") + " {");
            namespaceCount++; // this is so lazy lol but i hope it fixes | holy effort twin
            string[] nameSpaceSplit = namespaze.ToString().Split(".");
            if (nameSpaceSplit.Length == 0)
            {
                currentFile.WriteLine("namespace GlobalNamespace {");
            }
            else
            {
                for (int i = 0; i < nameSpaceSplit.Length; ++i)
                {
                    currentFile.WriteLine("namespace " + nameSpaceSplit[i] + " {");
                    namespaceCount++;
                }
            }

            currentFile.WriteLine();

            currentFile.WriteLine("class " + validClassname);
            currentFile.WriteLine("{");
            currentFile.WriteLine("public: ");

            currentFile.WriteLine();

            currentFile.WriteLine("\tstatic BNM::Class StaticClass() {");
            currentFile.WriteLine(string.Format("\t\treturn BNM::Class(\"{0}\", \"{1}\", BNM::Image(\"{2}\"));", namespaze, className, module.Name));
            currentFile.WriteLine("\t}");

            currentFile.WriteLine();

            ParseFields(clazz);

            currentFile.WriteLine();

            ParseMethods(clazz);

            currentFile.WriteLine();

            currentFile.WriteLine("};");
            currentFile.WriteLine();

            for (int i = 0; i < namespaceCount; ++i)
            {
                currentFile.WriteLine("}");
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