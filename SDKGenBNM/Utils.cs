using System.Text;
using System.Collections.Generic;
using dnlib;
using dnlib.DotNet;
using System.Text.RegularExpressions;
using System.Linq;
using System.ComponentModel;

public static class Utils
{
    public static bool StartsWithNumber(string str)
    {
        return char.IsDigit(str[0]);
    }
   public static string Il2CppTypeToCppType(TypeSig? type, TypeDef? parentType = null)
    {
        if (type == null)
            return "BNM::IL2CPP::Il2CppObject*";

        if (type.IsPointer)
            return "void* /*POINTER*/";

        if (type.IsGenericInstanceType)
            return "void* /*GENERICTYPE*/";

        if (type.ContainsGenericParameter)
            return FormatIl2CppGeneric(type);

        bool isArray = type.FullName.Contains("[]");

        TypeDef? tdef = type.TryGetTypeDef();
        bool isEnum = tdef?.IsEnum ?? false;

        string result = type.FullName switch
        {
            "System.Void" => "void",
            "System.Int8" => "int8_t",
            "System.UInt8" => "uint8_t",
            "System.Int16" => "short",
            "System.Int32" => "int",
            "System.Int64" => "int64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Boolean" => "bool",
            "System.Char" => "char",
            "System.UInt16" => "BNM::Types::ushort",
            "System.UInt32" => "BNM::Types::uint",
            "System.UInt64" => "BNM::Types::ulong",
            "System.Decimal" => "BNM::Types::decimal",
            "System.Byte" => "BNM::Types::byte",
            "System.SByte" => "BNM::Types::sbyte",
            "System.String" => "BNM::Structures::Mono::String*",
            "System.Type" => "BNM::MonoType*",
            "System.IntPtr" => "BNM::Types::nuint",
            "UnityEngine.Object" => "BNM::UnityEngine::Object*",
            "UnityEngine.MonoBehaviour" => "BNM::UnityEngine::MonoBehaviour*",
            "UnityEngine.Vector2" => "BNM::Structures::Unity::Vector2",
            "UnityEngine.Vector3" => "BNM::Structures::Unity::Vector3",
            "UnityEngine.Vector4" => "BNM::Structures::Unity::Vector4",
            "UnityEngine.Quaternion" => "BNM::Structures::Unity::Quaternion",
            "UnityEngine.Rect" => "BNM::Structures::Unity::Rect",
            "UnityEngine.Color" => "BNM::Structures::Unity::Color",
            "UnityEngine.Color32" => "BNM::Structures::Unity::Color32",
            "UnityEngine.Ray" => "BNM::Structures::Unity::Ray",
            "UnityEngine.RaycastHit" => "BNM::Structures::Unity::RaycastHit",
            _ => isEnum && tdef != null ? GetEnumType(tdef) : "BNM::IL2CPP::Il2CppObject*"
        };

        if (parentType != null && parentType.FullName == type.FullName)
        {
            string ns = string.IsNullOrEmpty(type.Namespace) ? "GlobalNamespace::" : type.Namespace.Replace(".", "::") + "::";

            result = $"{ns}{FormatInvalidName(type.GetName())}*";
        }


        if (isArray)
            result = $"BNM::Structures::Mono::Array<{result}>*";

        return result;
    }

    public static string GetEnumType(TypeDef clazz)
    {
        string type = "int";
        var ff = clazz.Fields.FirstOrDefault(x => x.FieldType != null && x.Name == "value__");
        if (ff != null)
        {
            type = Il2CppTypeToCppType(ff.FieldType);
        }
        return type;
    }

    public static string FormatIl2CppGeneric(TypeSig typeSig)
    {
        if (!typeSig.IsGenericInstanceType)
            return "void*";

        var genericInstSig = typeSig.ToGenericInstSig();
        var typeName = genericInstSig.GenericType.TypeName;

        string result = "";

        if (typeName.StartsWith("List"))
        {
            result = "BNM::Structures::Mono::List<";
        }
        else if (typeName.StartsWith("Dictionary"))
        {
            result = "BNM::Structures::Mono::Dictionary<";
        }
        else
        {
            return "void*";
        }

        List<string> args = new List<string>();
        foreach (var arg in genericInstSig.GenericArguments)
        {
            args.Add(Il2CppTypeToCppType(arg));
        }

        result += string.Join(", ", args);
        result += ">*";
        return result;
    }
    
    public static bool IsKeyword(string str)
    {
        string[] keywords = [
            "", "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept",
            "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
            "class", "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue",
            "contract_assert", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
            "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
            "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr",
            "operator", "or", "or_eq", "private", "protected", "public", "reflexpr", "register", "reinterpret_cast",
            "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct",
            "switch", "synchronized", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid",
            "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",

            "abstract", "add", "as", "base", "byte", "checked", "decimal", "delegate", "event", "explicit", "extern",
            "finally", "fixed", "foreach", "implicit", "in", "interface", "internal", "is", "lock", "null", "object",
            "out", "override", "params", "readonly", "ref", "remove", "sbyte", "sealed", "stackalloc", "string",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using static", "value", "when", "where",
            "yield",

            "INT32_MAX", "INT32_MIN", "UINT32_MAX", "UINT16_MAX", "INT16_MAX", "UINT8_MAX", "INT8_MAX", "INT_MAX",
            "Assert", "NULL", "O",
        ];
        return keywords.Contains(str);
    }

    public static string[] MakeValidParams(string[] paramNames)
    {
        var results = new List<string>();
        var seen = new Dictionary<string, int>();

        foreach (var param in paramNames)
        {
            string cparam = param;
            if (seen.ContainsKey(param))
            {
                seen[param]++;
                cparam = $"{param}_{seen[param]}";
            }
            else
            {
                seen[param] = 0;
            }

            results.Add(cparam);
        }

        return results.ToArray();
    }

    public static string FormatInvalidName(string className)
    {
        string str = className.Trim()
            .Replace("<", "$")
            .Replace(">", "$")
            .Replace("|", "$")
            .Replace("-", "$")
            .Replace("`", "$")
            .Replace("=", "$")
            .Replace("@", "$")
            .Replace("[]", "")
            .Replace(",", "")
            .Replace("{", "$")
            .Replace("}", "$")
            .Replace("(", "$")
            .Replace(")", "$")
            .Trim();
        
        if (string.IsNullOrEmpty(str))
            return "_";

        if (StartsWithNumber(str))
            str = "_" + str;

        if (IsKeyword(str))
            str = "$" + str;

        return str;
    }

    public static bool IsStruct(this TypeDef type)
    {
        return type.IsValueType && !type.IsEnum;
    }
}