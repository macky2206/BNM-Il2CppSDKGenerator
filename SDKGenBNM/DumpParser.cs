using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppSDK
{
    public class DumpClass
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string BaseType { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public List<DumpField> Fields { get; set; } = new List<DumpField>();
        public List<DumpMethod> Methods { get; set; } = new List<DumpMethod>();
        public bool IsEnum { get; set; }
        public bool IsStruct { get; set; }
        public bool IsSealed { get; set; }
        public bool IsAbstract { get; set; }
        public string Module { get; set; }
    }

    public class DumpField
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public bool IsStatic { get; set; }
        public string Offset { get; set; }
        public object ConstantValue { get; set; }
        public bool IsLiteral { get; set; }
    }

    public class DumpMethod
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public List<DumpParameter> Parameters { get; set; } = new List<DumpParameter>();
        public bool IsStatic { get; set; }
        public bool IsConstructor { get; set; }
        public string Offset { get; set; }
    }

    public class DumpParameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsOut { get; set; }
    }

    public class DumpParser
    {
        private Dictionary<string, List<DumpClass>> namespaces = new Dictionary<string, List<DumpClass>>();
        private string currentNamespace = "";
        private DumpClass currentClass = null;
        private string currentModule = "";
        private bool insideClass = false;
        private int braceLevel = 0;

        public Dictionary<string, List<DumpClass>> ParseDumpFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                if (string.IsNullOrEmpty(line))
                    continue;

                ParseLine(line);
            }

            return namespaces;
        }

        private void ParseLine(string line)
        {
            // Parse module/dll information
            if (line.StartsWith("// Dll : "))
            {
                currentModule = line.Substring("// Dll : ".Length).Trim();
                return;
            }

            // Parse namespace
            if (line.StartsWith("// Namespace: "))
            {
                currentNamespace = line.Substring("// Namespace: ".Length).Trim();
                if (string.IsNullOrEmpty(currentNamespace))
                {
                    currentNamespace = "GlobalNamespace";
                }
                return;
            }

            // Track brace levels
            if (line.Contains("{"))
                braceLevel++;
            if (line.Contains("}"))
            {
                braceLevel--;
                if (braceLevel <= 0 && insideClass)
                {
                    insideClass = false;
                    currentClass = null;
                }
            }

            // Parse class/struct/enum declarations
            if (!insideClass && (line.Contains(" class ") || line.Contains(" struct ") || line.Contains("enum ")))
            {
                ParseClassDeclaration(line);
                return;
            }

            // Parse fields and methods inside class
            if (insideClass && currentClass != null)
            {
                if (line.Contains("; // 0x"))
                {
                    ParseField(line);
                }
                else if (line.Contains("() { }") || line.Contains(") { }"))
                {
                    ParseMethod(line);
                }
                else if (currentClass.IsEnum && line.Contains(" = "))
                {
                    ParseEnumField(line);
                }
            }
        }

        private void ParseClassDeclaration(string line)
        {
            var classObj = new DumpClass();
            classObj.Namespace = currentNamespace;
            classObj.Module = currentModule;

            // Parse modifiers and class name
            if (line.Contains(" class "))
            {
                var parts = line.Split(new[] { " class " }, StringSplitOptions.None);
                var modifierPart = parts[0].Trim();
                var namePart = parts[1].Trim();

                // Extract modifiers
                var modifiers = modifierPart.Split(' ').Where(m => !string.IsNullOrEmpty(m)).ToList();
                classObj.Modifiers = modifiers;
                classObj.IsSealed = modifiers.Contains("sealed");
                classObj.IsAbstract = modifiers.Contains("abstract");

                // Extract class name and inheritance
                if (namePart.Contains(" : "))
                {
                    var nameAndBase = namePart.Split(new[] { " : " }, StringSplitOptions.None);
                    classObj.Name = nameAndBase[0].Trim();
                    classObj.BaseType = nameAndBase[1].Trim();
                }
                else
                {
                    classObj.Name = namePart.Replace("{", "").Trim();
                }
            }
            else if (line.Contains(" struct "))
            {
                var parts = line.Split(new[] { " struct " }, StringSplitOptions.None);
                var modifierPart = parts[0].Trim();
                var namePart = parts[1].Trim();

                classObj.Modifiers = modifierPart.Split(' ').Where(m => !string.IsNullOrEmpty(m)).ToList();
                classObj.Name = namePart.Replace("{", "").Trim();
                classObj.IsStruct = true;
            }
            else if (line.Contains("enum "))
            {
                var match = Regex.Match(line, @"enum\s+(\w+)");
                if (match.Success)
                {
                    classObj.Name = match.Groups[1].Value;
                    classObj.IsEnum = true;
                }
            }

            // Clean up class name
            classObj.Name = CleanName(classObj.Name);

            // Add to namespace
            if (!namespaces.ContainsKey(currentNamespace))
            {
                namespaces[currentNamespace] = new List<DumpClass>();
            }
            namespaces[currentNamespace].Add(classObj);

            currentClass = classObj;
            insideClass = true;
        }

        private void ParseField(string line)
        {
            if (currentClass == null) return;

            try
            {
                // Pattern: [modifiers] type fieldName; // 0xOffset
                var match = Regex.Match(line, @"^(.+?)\s+(\w+);\s*//\s*0x([A-Fa-f0-9]+)");
                if (match.Success)
                {
                    var typeAndModifiers = match.Groups[1].Value.Trim();
                    var fieldName = match.Groups[2].Value;
                    var offset = match.Groups[3].Value;

                    var field = new DumpField();
                    field.Name = fieldName;
                    field.Offset = offset;

                    // Parse modifiers and type
                    var parts = typeAndModifiers.Split(' ').Where(p => !string.IsNullOrEmpty(p)).ToList();
                    
                    // Extract modifiers
                    var modifiers = new List<string>();
                    string fieldType = "";
                    
                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] == "public" || parts[i] == "private" || parts[i] == "protected" ||
                            parts[i] == "static" || parts[i] == "readonly" || parts[i] == "const")
                        {
                            modifiers.Add(parts[i]);
                            if (parts[i] == "static")
                                field.IsStatic = true;
                        }
                        else
                        {
                            // Remaining parts form the type
                            fieldType = string.Join(" ", parts.Skip(i));
                            break;
                        }
                    }

                    field.Type = fieldType;
                    field.Modifiers = modifiers;
                    currentClass.Fields.Add(field);
                }
            }
            catch (Exception)
            {
                // Ignore malformed field lines
            }
        }

        private void ParseMethod(string line)
        {
            if (currentClass == null) return;

            try
            {
                // Pattern: [modifiers] returnType methodName(parameters) { }
                var match = Regex.Match(line, @"^(.+?)\s+(\w+)\s*\(([^)]*)\)\s*\{\s*\}");
                if (match.Success)
                {
                    var returnTypeAndModifiers = match.Groups[1].Value.Trim();
                    var methodName = match.Groups[2].Value;
                    var parametersStr = match.Groups[3].Value.Trim();

                    var method = new DumpMethod();
                    method.Name = methodName;

                    // Parse modifiers and return type
                    var parts = returnTypeAndModifiers.Split(' ').Where(p => !string.IsNullOrEmpty(p)).ToList();
                    
                    var modifiers = new List<string>();
                    string returnType = "";
                    
                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] == "public" || parts[i] == "private" || parts[i] == "protected" ||
                            parts[i] == "static" || parts[i] == "virtual" || parts[i] == "override" ||
                            parts[i] == "abstract" || parts[i] == "sealed")
                        {
                            modifiers.Add(parts[i]);
                            if (parts[i] == "static")
                                method.IsStatic = true;
                        }
                        else
                        {
                            returnType = string.Join(" ", parts.Skip(i));
                            break;
                        }
                    }

                    method.ReturnType = returnType;
                    method.Modifiers = modifiers;
                    method.IsConstructor = methodName == ".ctor" || methodName == currentClass.Name;

                    // Parse parameters
                    if (!string.IsNullOrEmpty(parametersStr))
                    {
                        var paramParts = parametersStr.Split(',');
                        foreach (var paramPart in paramParts)
                        {
                            var paramTrimmed = paramPart.Trim();
                            if (!string.IsNullOrEmpty(paramTrimmed))
                            {
                                var paramTokens = paramTrimmed.Split(' ').Where(t => !string.IsNullOrEmpty(t)).ToArray();
                                if (paramTokens.Length >= 2)
                                {
                                    var param = new DumpParameter();
                                    param.Type = string.Join(" ", paramTokens.Take(paramTokens.Length - 1));
                                    param.Name = paramTokens.Last();
                                    param.IsOut = param.Type.Contains("out ");
                                    method.Parameters.Add(param);
                                }
                            }
                        }
                    }

                    currentClass.Methods.Add(method);
                }
            }
            catch (Exception)
            {
                // Ignore malformed method lines
            }
        }

        private void ParseEnumField(string line)
        {
            if (currentClass == null || !currentClass.IsEnum) return;

            try
            {
                // Pattern: fieldName = value,
                var match = Regex.Match(line, @"(\w+)\s*=\s*([^,]+)");
                if (match.Success)
                {
                    var fieldName = match.Groups[1].Value;
                    var value = match.Groups[2].Value.Replace(",", "").Trim();

                    var field = new DumpField();
                    field.Name = fieldName;
                    field.Type = "int"; // Default enum type
                    field.IsLiteral = true;
                    field.IsStatic = true;
                    
                    // Try to parse numeric value
                    if (int.TryParse(value, out int intValue))
                    {
                        field.ConstantValue = intValue;
                    }
                    else
                    {
                        field.ConstantValue = value;
                    }

                    currentClass.Fields.Add(field);
                }
            }
            catch (Exception)
            {
                // Ignore malformed enum field lines
            }
        }

        private string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";

            return name.Replace("<", "").Replace(">", "").Replace("`", "").Replace("{", "").Replace("}", "").Trim();
        }
    }
}
