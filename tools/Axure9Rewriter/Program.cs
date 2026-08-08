using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class Program
{
    private const int LoadMethodToken = 0x0600B166;
    private const int SaveMethodToken = 0x0600B167;
    private const int UseMemoryStreamFieldToken = 0x04002900;
    private static string _axureDirectory = "";

    private static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "Usage: Axure9Rewriter <Axure9 directory> <source package> <RP9 template package> <output>");
            return 2;
        }

        _axureDirectory = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;

        try
        {
            var assembly = Assembly.LoadFrom(Path.Combine(_axureDirectory, "AxureRP9.exe"));
            var serializerType = assembly.GetType("Pacj.jac4", throwOnError: true);
            var singletonMethod = serializerType.GetMethod(
                "S4hl",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var serializer = singletonMethod.Invoke(null, null);
            var loadMethod = serializerType.Module.ResolveMethod(LoadMethodToken) as MethodInfo;
            var saveMethod = serializerType.Module.ResolveMethod(SaveMethodToken) as MethodInfo;
            serializerType.Module
                .ResolveField(UseMemoryStreamFieldToken)
                .SetValue(null, true);

            var source = LoadPackage(serializer, loadMethod, args[1]);
            var template = LoadPackage(serializer, loadMethod, args[2]);
            var versionField = source.GetType().GetField(
                "qatb",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var targetVersion = versionField.GetValue(template);
            versionField.SetValue(source, targetVersion);
            var removedInteractions = StripInteractions(source);

            using (var output = new MemoryStream())
            {
                saveMethod.Invoke(serializer, new[] { source, output });
                File.WriteAllBytes(Path.GetFullPath(args[3]), output.ToArray());
                Console.WriteLine(
                    "SUCCESS version={0} bytes={1} removedInteractions={2}",
                    targetVersion,
                    output.Length,
                    removedInteractions);
            }
            return 0;
        }
        catch (TargetInvocationException error)
        {
            Console.Error.WriteLine(error.InnerException ?? error);
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static object LoadPackage(object serializer, MethodInfo loadMethod, string path)
    {
        var bytes = File.ReadAllBytes(Path.GetFullPath(path));
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            return loadMethod.Invoke(serializer, new object[] { stream, 96.0, false });
        }
    }

    private static int StripInteractions(object packageContext)
    {
        var removed = 0;
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField.GetValue(packageContext) as IList;
        if (objects == null)
        {
            return 0;
        }

        for (var index = objects.Count - 1; index >= 0; index--)
        {
            var item = objects[index];
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var typeName = typeNameField == null
                ? ""
                : Convert.ToString(typeNameField.GetValue(item));
            if (typeName.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                objects.RemoveAt(index);
                removed++;
                continue;
            }

            var dictionaryField = FindField(item.GetType(), "sadF");
            var dictionary = dictionaryField == null
                ? null
                : dictionaryField.GetValue(item) as IDictionary;
            if (dictionary == null)
            {
                continue;
            }

            var keysToRemove = new List<object>();
            foreach (DictionaryEntry pair in dictionary)
            {
                var key = Convert.ToString(pair.Key);
                if (key.IndexOf("interaction", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    keysToRemove.Add(pair.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
                removed++;
            }
        }
        return removed;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field;
            }
            type = type.BaseType;
        }
        return null;
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
}
