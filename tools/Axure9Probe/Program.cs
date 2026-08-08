using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class Program
{
    private static string _axureDirectory = "";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Axure9Probe <Axure9 directory> <decoded package>");
            return 2;
        }

        _axureDirectory = Path.GetFullPath(args[0]);
        var packagePath = Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;

        try
        {
            var assembly = Assembly.LoadFrom(Path.Combine(_axureDirectory, "AxureRP9.exe"));
            var serializerType = assembly.GetType("Pacj.jac4", throwOnError: true);
            var singletonMethod = serializerType.GetMethod(
                "S4hl",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var loadMethod = serializerType.Module.ResolveMethod(0x0600B166) as MethodInfo;
            if (loadMethod == null)
            {
                Console.Error.WriteLine(
                    "Could not locate the three-argument Load method. Available methods:");
                foreach (var method in serializerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Console.Error.WriteLine(
                        "  token={0:X8} name={1} return={2} params={3} signature={4}",
                        method.MetadataToken,
                        method.Name,
                        method.ReturnType,
                        method.GetParameters().Length,
                        method);
                }
                return 1;
            }

            var serializer = singletonMethod.Invoke(null, null);
            var bytes = File.ReadAllBytes(packagePath);

            object[] candidates =
            {
                bytes,
                new MemoryStream(bytes, writable: false),
                new BinaryReader(new MemoryStream(bytes, writable: false)),
                packagePath
            };

            foreach (var candidate in candidates)
            {
                var candidateName = candidate.GetType().FullName;
                try
                {
                    var result = loadMethod.Invoke(
                        serializer,
                        new[] { candidate, (object)96.0, false });
                    Console.WriteLine(
                        "SUCCESS input={0} result={1}",
                        candidateName,
                        result == null ? "<null>" : result.GetType().FullName);
                    DescribeResult(result);
                    return 0;
                }
                catch (TargetInvocationException error)
                {
                    var cause = error.InnerException ?? error;
                    Console.WriteLine(
                        "FAIL input={0} error={1}: {2}",
                        candidateName,
                        cause.GetType().FullName,
                        cause.Message);
                }
            }

            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Assembly ResolveAxureAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        var dllPath = Path.Combine(_axureDirectory, name + ".dll");
        if (File.Exists(dllPath))
        {
            return Assembly.LoadFrom(dllPath);
        }

        var exePath = Path.Combine(_axureDirectory, name + ".exe");
        return File.Exists(exePath) ? Assembly.LoadFrom(exePath) : null;
    }

    private static void DescribeResult(object result)
    {
        if (result == null)
        {
            return;
        }

        var type = result.GetType();
        foreach (var field in type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                var value = field.GetValue(result);
                Console.WriteLine(
                    "FIELD {0} type={1} value={2}",
                    field.Name,
                    field.FieldType.FullName,
                    Summarize(value));
                if (value != null && field.Name == "Watr")
                {
                    DescribeObject("ROOT", value);
                    DescribeEnumerable("ROOT-ITEM", value as IEnumerable);
                }
                if (value != null && field.Name == "yatu")
                {
                    DescribeEnumerable("OBJECT", value as IEnumerable);
                }
            }
            catch
            {
            }
        }

        foreach (var property in type.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(result, null);
                Console.WriteLine(
                    "PROPERTY {0}={1}",
                    property.Name,
                    value == null ? "<null>" : value.ToString());
            }
            catch
            {
                // Some document properties require a fully initialized Axure host.
            }
        }

        foreach (var method in type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.ReturnType.FullName == "System.Void" ||
                method.GetParameters().Length != 0 ||
                method.IsSpecialName)
            {
                continue;
            }

            try
            {
                var value = method.Invoke(result, null);
                Console.WriteLine(
                    "METHOD {0}() type={1} value={2}",
                    method.Name,
                    method.ReturnType.FullName,
                    Summarize(value));
            }
            catch
            {
            }
        }
    }

    private static void DescribeObject(string label, object value)
    {
        Console.WriteLine("{0} runtimeType={1}", label, value.GetType().FullName);
        foreach (var field in value.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                Console.WriteLine(
                    "  FIELD {0} type={1} value={2}",
                    field.Name,
                    field.FieldType.FullName,
                    Summarize(field.GetValue(value)));
            }
            catch
            {
            }
        }
    }

    private static void DescribeEnumerable(string label, IEnumerable values)
    {
        if (values == null)
        {
            return;
        }

        var index = 0;
        foreach (var item in values)
        {
            if (index >= 40)
            {
                break;
            }
            Console.WriteLine(
                "{0}[{1}] type={2} value={3}",
                label,
                index,
                item == null ? "<null>" : item.GetType().FullName,
                Summarize(item));
            if (item != null)
            {
                DescribeObject("  ITEM", item);
                if (item.GetType().FullName == "aadD.aadq")
                {
                    PrintPairs(item as IEnumerable);
                }
            }
            index++;
        }
    }

    private static void PrintPairs(IEnumerable pairs)
    {
        PrintPairs(pairs, "    ", 0);
    }

    private static void PrintPairs(IEnumerable pairs, string indent, int depth)
    {
        if (pairs == null)
        {
            return;
        }

        foreach (var pair in pairs)
        {
            if (pair == null)
            {
                continue;
            }
            var pairType = pair.GetType();
            var keyField = pairType.GetField(
                "key",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var valueField = pairType.GetField(
                "value",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (keyField == null || valueField == null)
            {
                Console.WriteLine(
                    "{0}ITEM value={1} valueType={2}",
                    indent,
                    Summarize(pair),
                    pairType.FullName);
                DescribeObject(indent + "  ITEM", pair);
                if (depth < 4 && pair is IEnumerable && !(pair is string))
                {
                    PrintPairs((IEnumerable)pair, indent + "  ", depth + 1);
                }
                continue;
            }
            var key = keyField.GetValue(pair);
            var value = valueField.GetValue(pair);
            Console.WriteLine(
                "{0}PROP {1}={2} valueType={3}",
                indent,
                key,
                Summarize(value),
                value == null ? "<null>" : value.GetType().FullName);
            if (depth < 5 && value is IEnumerable && !(value is string))
            {
                PrintPairs((IEnumerable)value, indent + "  ", depth + 1);
            }
        }
    }

    private static string Summarize(object value)
    {
        if (value == null)
        {
            return "<null>";
        }
        if (value is string || value.GetType().IsPrimitive || value is Guid)
        {
            return value.ToString();
        }
        var collection = value as ICollection;
        if (collection != null)
        {
            return string.Format("{0} Count={1}", value.GetType().FullName, collection.Count);
        }
        var enumerable = value as IEnumerable;
        if (enumerable != null)
        {
            var count = 0;
            foreach (var ignored in enumerable)
            {
                count++;
                if (count == 1000)
                {
                    break;
                }
            }
            return string.Format("{0} Items~{1}", value.GetType().FullName, count);
        }
        return value.ToString();
    }
}
