using K4os.Compression.LZ4.Legacy;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal static class Program
{
    private const string Rp9VersionPartName = "9.0.0.3754.version";
    private const int LoadMethodToken = 0x0600B166;
    private const int SaveMethodToken = 0x0600B167;
    private const int UseMemoryStreamFieldToken = 0x04002900;
    private static string _axureDirectory = "";
    private static object _serializer;
    private static MethodInfo _loadMethod;
    private static MethodInfo _saveMethod;
    private static object _targetVersion;
    private static int _pagesRewritten;
    private static int _objectPackagesRewritten;
    private static int _designDocumentsRewritten;
    private static int _settingsRewritten;
    private static int _staticRecordsVerified;
    private static int _staticScalarsVerified;
    private static int _unsupportedStylePropertiesRemoved;
    private static int _rp9RequiredFieldsAdded;
    private static int _unsupportedWorkspaceTabsRemoved;
    private static int _unsupportedSettingsPropertiesRemoved;
    private static int _gzipParts;
    private static int _lastProgress = -1;
    private static string _lastProgressStage = "";
    private static readonly HashSet<string> ScannedKeys = new HashSet<string>();
    private static int _styleGraphsPrinted;
    private const string Rp9BreakingChanges =
@"<BreakingChanges>
    <Change Fail='True' UseMessage='False' Beta='false'>
        <Version>9.0.0.0</Version>
        <Message>This file was created with Axure RP 9.0, and cannot be opened in earlier versions.

If you are using the Axure RP 9.0 Beta, please update to the latest beta version.

Please visit www.axure.com to upgrade to the latest version of Axure RP.</Message>
        <TeamMessage>This team project has been upgraded to a newer version of Axure RP.
To use this team project, you need to upgrade to version 9.0 or above.

If you are using the Axure RP 9.0 Beta, please update to the latest beta version.

Please visit www.axure.com to upgrade to the latest version of Axure RP.</TeamMessage>
    </Change>
</BreakingChanges>
";

    private sealed class Part
    {
        public Dictionary<string, object> Parent;
        public string Key;
        public int Offset;
        public byte[] Gzip;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--scan-container")
        {
            try
            {
                _axureDirectory = Path.GetFullPath(args[1]);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
                InitializeAxureWriter();
                ScanContainer(Path.GetFullPath(args[2]));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 4 && args[0] == "--extract-part")
        {
            try
            {
                ExtractPart(
                    Path.GetFullPath(args[1]),
                    args[2],
                    Path.GetFullPath(args[3]));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--inventory")
        {
            try
            {
                _axureDirectory = Path.GetFullPath(args[1]);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
                InitializeAxureWriter();
                InventoryContainer(Path.GetFullPath(args[2]));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 5 && args[0] == "--set-first-text")
        {
            try
            {
                _axureDirectory = Path.GetFullPath(args[1]);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
                InitializeAxureWriter();
                SetFirstTextInContainer(
                    Path.GetFullPath(args[2]),
                    Path.GetFullPath(args[3]),
                    args[4]);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 5 && args[0] == "--hybrid")
        {
            try
            {
                BuildHybrid(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2]),
                    Path.GetFullPath(args[3]),
                    args[4]);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--scan-keys")
        {
            try
            {
                _axureDirectory = Path.GetFullPath(args[1]);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
                InitializeAxureWriter();
                ScanKeys(Path.GetFullPath(args[2]));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length == 2 && args[0] == "--dump-header")
        {
            try
            {
                DumpHeader(Path.GetFullPath(args[1]));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: AxureContainerRewriter <Axure9 directory> <source.rp> <output.rp>\n" +
                "       AxureContainerRewriter --dump-header <file.rp>\n" +
                "       AxureContainerRewriter --extract-part <file.rp> <kind> <output>\n" +
                "       AxureContainerRewriter --inventory <Axure9 directory> <file.rp>\n" +
                "       AxureContainerRewriter --scan-container <Axure9 directory> <file.rp>\n" +
                "       AxureContainerRewriter --scan-keys <Axure9 directory> <decoded package>\n" +
                "       AxureContainerRewriter --hybrid <base.rp> <donor.rp> <output.rp> <kinds>\n" +
                "       AxureContainerRewriter --set-first-text <Axure9 directory> <input.rp> <output.rp> <text>");
            return 2;
        }

        try
        {
            _axureDirectory = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
            ReportProgress(14, "initialize_serializer");
            InitializeAxureWriter();
            ReportProgress(20, "read_container");
            Rewrite(
                Path.GetFullPath(args[1]),
                Path.GetFullPath(args[2]));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void SetFirstTextInContainer(
        string inputPath,
        string outputPath,
        string text)
    {
        Dictionary<string, object> header;
        List<Part> parts;
        ushort formatMajor;
        ReadContainer(inputPath, out header, out parts, out formatMajor);
        var page = parts.FirstOrDefault(part => ClassifyPart(part) == "page");
        if (page == null)
        {
            throw new InvalidDataException("No page package was found.");
        }

        var decoded = Gunzip(page.Gzip);
        object packageContext;
        using (var input = new MemoryStream(decoded, writable: false))
        {
            packageContext = _loadMethod.Invoke(
                _serializer,
                new object[] { input, 96.0, false });
        }
        if (!SetFirstVectorShapeText(packageContext, text))
        {
            throw new InvalidDataException(
                "No editable VectorShape text property was found.");
        }
        using (var output = new MemoryStream())
        {
            _saveMethod.Invoke(_serializer, new[] { packageContext, output });
            page.Gzip = Gzip(output.ToArray());
        }
        WriteContainer(header, parts, outputPath, formatMajor);
        Console.WriteLine("TEXT-UPDATED value={0}", text);
    }

    private static bool SetFirstVectorShapeText(
        object packageContext,
        string text)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return false;
        }
        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (typeNameField == null ||
                Convert.ToString(typeNameField.GetValue(item)) !=
                    "Axure:DiagramObject:VectorShape")
            {
                continue;
            }
            var visited = new HashSet<object>(ReferenceComparer.Instance);
            if (SetTextRecursive(item, text, 0, visited))
            {
                return true;
            }
        }
        return false;
    }

    private static bool SetTextRecursive(
        object value,
        string text,
        int depth,
        HashSet<object> visited)
    {
        if (value == null || value is string || value is byte[] ||
            depth > 24 || !visited.Add(value))
        {
            return false;
        }
        var enumerable = value as IEnumerable;
        if (enumerable != null)
        {
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }
                var itemType = item.GetType();
                var keyField = itemType.GetField(
                    "key",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var valueField = itemType.GetField(
                    "value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (keyField != null && valueField != null)
                {
                    var child = valueField.GetValue(item);
                    if (Convert.ToString(keyField.GetValue(item)) == "text")
                    {
                        var dictionaryField = FindField(value.GetType(), "sadF");
                        var dictionary = dictionaryField == null
                            ? null
                            : dictionaryField.GetValue(value) as IDictionary;
                        var constructor = child == null
                            ? null
                            : child.GetType().GetConstructor(
                                BindingFlags.Instance |
                                BindingFlags.Public |
                                BindingFlags.NonPublic,
                                null,
                                new[] { typeof(string) },
                                null);
                        if (dictionary != null && constructor != null)
                        {
                            foreach (DictionaryEntry pair in dictionary)
                            {
                                if (Convert.ToString(pair.Key) == "text")
                                {
                                    dictionary[pair.Key] =
                                        constructor.Invoke(new object[] { text });
                                    visited.Remove(value);
                                    return true;
                                }
                            }
                        }
                    }
                    if (SetTextRecursive(child, text, depth + 1, visited))
                    {
                        visited.Remove(value);
                        return true;
                    }
                }
                else if (SetTextRecursive(item, text, depth + 1, visited))
                {
                    visited.Remove(value);
                    return true;
                }
            }
        }
        visited.Remove(value);
        return false;
    }

    private static void BuildHybrid(
        string basePath,
        string donorPath,
        string outputPath,
        string kinds)
    {
        Dictionary<string, object> header;
        List<Part> parts;
        ushort formatMajor;
        ReadContainer(basePath, out header, out parts, out formatMajor);
        Dictionary<string, object> donorHeader;
        List<Part> donorParts;
        ushort donorFormatMajor;
        ReadContainer(
            donorPath,
            out donorHeader,
            out donorParts,
            out donorFormatMajor);

        var requested = new HashSet<string>(
            kinds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var donorByKind = donorParts
            .GroupBy(ClassifyPart)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var replaced = 0;
        foreach (var part in parts)
        {
            var kind = ClassifyPart(part);
            Part donor;
            if (requested.Contains(kind) && donorByKind.TryGetValue(kind, out donor))
            {
                part.Gzip = donor.Gzip;
                replaced++;
            }
        }
        if (replaced == 0)
        {
            throw new InvalidDataException("No requested parts were replaced.");
        }
        WriteContainer(header, parts, outputPath, formatMajor);
        Console.WriteLine("HYBRID replaced={0} kinds={1}", replaced, kinds);
    }

    private static void ReadContainer(
        string path,
        out Dictionary<string, object> header,
        out List<Part> parts,
        out ushort formatMajor)
    {
        var source = File.ReadAllBytes(path);
        if (source.Length < 12 || source[0] != 0xAC || source[1] != 0xEF)
        {
            throw new InvalidDataException("Input is not an Axure RP container.");
        }
        formatMajor = BitConverter.ToUInt16(source, 2);
        var headerLength = BitConverter.ToInt32(source, 4);
        var compressedHeader = new byte[headerLength];
        Buffer.BlockCopy(source, 8, compressedHeader, 0, headerLength);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        header = serializer.Deserialize<Dictionary<string, object>>(
            Encoding.UTF8.GetString(DecodeLz4(compressedHeader)));
        parts = new List<Part>();
        CollectParts(header, parts);
        parts.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        var dataBase = 8 + headerLength + 4;
        foreach (var part in parts)
        {
            var recordOffset = dataBase + part.Offset;
            var payloadLength = BitConverter.ToInt32(source, recordOffset);
            part.Gzip = new byte[payloadLength];
            Buffer.BlockCopy(source, recordOffset + 4, part.Gzip, 0, payloadLength);
        }
    }

    private static void WriteContainer(
        Dictionary<string, object> header,
        List<Part> parts,
        string outputPath,
        ushort formatMajor)
    {
        var nextOffset = 0;
        foreach (var part in parts)
        {
            part.Offset = nextOffset;
            part.Parent[part.Key] = nextOffset;
            nextOffset += 4 + part.Gzip.Length + 4;
        }
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var rewrittenHeader = EncodeLz4(
            Encoding.UTF8.GetBytes(serializer.Serialize(header)));
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
        {
            writer.Write((byte)0xAC);
            writer.Write((byte)0xEF);
            writer.Write(formatMajor);
            writer.Write(rewrittenHeader.Length);
            writer.Write(rewrittenHeader);
            writer.Write(0);
            for (var index = 0; index < parts.Count; index++)
            {
                writer.Write(parts[index].Gzip.Length);
                writer.Write(parts[index].Gzip);
                if (index + 1 < parts.Count)
                {
                    writer.Write(0);
                }
            }
            writer.Flush();
            File.WriteAllBytes(outputPath, output.ToArray());
        }
    }

    private static string ClassifyPart(Part part)
    {
        if (part.Key.EndsWith(".version", StringComparison.OrdinalIgnoreCase))
        {
            return "version";
        }
        if (part.Key.Equals("thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return "thumbnail";
        }
        if (part.Gzip.Length < 3 ||
            part.Gzip[0] != 0x1F || part.Gzip[1] != 0x8B || part.Gzip[2] != 0x08)
        {
            return "binary";
        }
        var decoded = Gunzip(part.Gzip);
        if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:DesignDocument")))
        {
            return "design";
        }
        if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:DocumentSettings")))
        {
            return "settings";
        }
        if (ContainsAsciiToken(
            decoded,
            Encoding.ASCII.GetBytes("Axure:Page")))
        {
            return "page";
        }
        if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:Page")))
        {
            return "object";
        }
        return part.Key;
    }

    private static void ScanKeys(string packagePath)
    {
        object packageContext;
        using (var input = new MemoryStream(File.ReadAllBytes(packagePath), writable: false))
        {
            packageContext = _loadMethod.Invoke(
                _serializer,
                new object[] { input, 96.0, false });
        }
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects != null)
        {
            foreach (var item in objects)
            {
                if (item == null)
                {
                    continue;
                }
                var typeNameField = item.GetType().GetField(
                    "Qaup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (typeNameField != null &&
                    Convert.ToString(typeNameField.GetValue(item)) == "Axure:PackageInfo")
                {
                    foreach (var method in item.GetType().GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method => method.Name == "Add"))
                    {
                        Console.WriteLine(
                            "ADD signature={0}({1})",
                            method.ReturnType.FullName,
                            string.Join(",", method.GetParameters()
                                .Select(parameter => parameter.ParameterType.FullName)
                                .ToArray()));
                    }
                    break;
                }
            }
        }
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        ScanKeysRecursive(objects, 0, visited);
    }

    private static void ScanContainer(string containerPath)
    {
        Dictionary<string, object> header;
        List<Part> parts;
        ushort formatMajor;
        ReadContainer(
            containerPath,
            out header,
            out parts,
            out formatMajor);
        foreach (var part in parts)
        {
            var kind = ClassifyPart(part);
            if (kind != "page" && kind != "object" &&
                kind != "design" && kind != "settings")
            {
                continue;
            }
            object packageContext;
            using (var input = new MemoryStream(
                Gunzip(part.Gzip),
                writable: false))
            {
                packageContext = _loadMethod.Invoke(
                    _serializer,
                    new object[] { input, 96.0, false });
            }
            if (kind == "page" &&
                !ContainsRecordType(packageContext, "Axure:Page"))
            {
                kind = "object";
            }
            Console.WriteLine(
                "PACKAGE kind={0} key={1}",
                kind,
                part.Key);
            var objectsField = packageContext.GetType().GetField(
                "yatu",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            var objects = objectsField == null
                ? null
                : objectsField.GetValue(packageContext);
            var visited = new HashSet<object>(ReferenceComparer.Instance);
            ScanKeysRecursive(objects, 0, visited);
        }
    }

    private static void InventoryContainer(string containerPath)
    {
        Dictionary<string, object> header;
        List<Part> parts;
        ushort formatMajor;
        ReadContainer(
            containerPath,
            out header,
            out parts,
            out formatMajor);

        var totalTypes = new SortedDictionary<string, int>(
            StringComparer.Ordinal);
        var totalKeys = new SortedDictionary<string, SortedSet<string>>(
            StringComparer.Ordinal);
        var packages = new List<object>();
        foreach (var part in parts)
        {
            var kind = ClassifyPart(part);
            if (kind != "page" && kind != "object" &&
                kind != "design" && kind != "settings")
            {
                continue;
            }

            object packageContext;
            using (var input = new MemoryStream(
                Gunzip(part.Gzip),
                writable: false))
            {
                packageContext = _loadMethod.Invoke(
                    _serializer,
                    new object[] { input, 96.0, false });
            }
            if (kind == "page" &&
                !ContainsRecordType(packageContext, "Axure:Page"))
            {
                kind = "object";
            }

            var packageTypes = new SortedDictionary<string, int>(
                StringComparer.Ordinal);
            var packageVersionField = packageContext.GetType().GetField(
                "qatb",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            var packageVersion = packageVersionField == null
                ? ""
                : Convert.ToString(packageVersionField.GetValue(packageContext));
            var objectsField = packageContext.GetType().GetField(
                "yatu",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            var objects = objectsField == null
                ? null
                : objectsField.GetValue(packageContext) as IEnumerable;
            var recordCount = 0;
            if (objects != null)
            {
                foreach (var item in objects)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    recordCount++;
                    var typeNameField = item.GetType().GetField(
                        "Qaup",
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);
                    var typeName = typeNameField == null
                        ? item.GetType().FullName
                        : Convert.ToString(typeNameField.GetValue(item));
                    if (string.IsNullOrWhiteSpace(typeName))
                    {
                        typeName = "<unknown>";
                    }
                    IncrementCount(packageTypes, typeName);
                    IncrementCount(totalTypes, typeName);
                    SortedSet<string> keys;
                    if (!totalKeys.TryGetValue(typeName, out keys))
                    {
                        keys = new SortedSet<string>(StringComparer.Ordinal);
                        totalKeys[typeName] = keys;
                    }
                    var dictionaryField = FindField(item.GetType(), "sadF");
                    var dictionary = dictionaryField == null
                        ? null
                        : dictionaryField.GetValue(item) as IDictionary;
                    if (dictionary != null)
                    {
                        foreach (DictionaryEntry pair in dictionary)
                        {
                            keys.Add(Convert.ToString(pair.Key));
                        }
                    }
                }
            }
            packages.Add(new
            {
                kind,
                key = part.Key,
                version = packageVersion,
                recordCount,
                recordTypes = packageTypes
            });
        }

        var serializer = new JavaScriptSerializer();
        serializer.MaxJsonLength = int.MaxValue;
        Console.WriteLine(serializer.Serialize(new
        {
            file = containerPath,
            formatMajor,
            packages,
            recordTypes = totalTypes,
            recordKeys = totalKeys
        }));
    }

    private static void ExtractPart(
        string containerPath,
        string requestedKind,
        string outputPath)
    {
        Dictionary<string, object> header;
        List<Part> parts;
        ushort formatMajor;
        ReadContainer(
            containerPath,
            out header,
            out parts,
            out formatMajor);
        var part = parts.FirstOrDefault(candidate =>
            string.Equals(
                ClassifyPart(candidate),
                requestedKind,
                StringComparison.OrdinalIgnoreCase));
        if (part == null)
        {
            throw new InvalidDataException(
                "No part of kind '" + requestedKind + "' was found.");
        }
        var bytes = part.Gzip.Length >= 3 &&
            part.Gzip[0] == 0x1F &&
            part.Gzip[1] == 0x8B &&
            part.Gzip[2] == 0x08
            ? Gunzip(part.Gzip)
            : part.Gzip;
        File.WriteAllBytes(outputPath, bytes);
        Console.WriteLine(
            "EXTRACTED kind={0} key={1} bytes={2} formatMajor={3}",
            requestedKind,
            part.Key,
            bytes.Length,
            formatMajor);
    }

    private static void IncrementCount(
        IDictionary<string, int> counts,
        string key)
    {
        int current;
        counts.TryGetValue(key, out current);
        counts[key] = current + 1;
    }

    private static void ScanKeysRecursive(
        object value,
        int depth,
        HashSet<object> visited)
    {
        if (value == null || value is string || value is byte[] || depth > 32)
        {
            if (HasUnsupportedStyleId(value))
            {
                Console.WriteLine(
                    "VALUE depth={0} value={1} valueType={2} fields={3} properties={4}",
                    depth,
                    value,
                    value == null ? "<null>" : value.GetType().FullName,
                    DescribeFields(value),
                    DescribeProperties(value));
            }
            return;
        }
        var enumerable = value as IEnumerable;
        if (enumerable == null)
        {
            if (HasUnsupportedStyleId(value))
            {
                Console.WriteLine(
                    "VALUE depth={0} value={1} valueType={2} fields={3} properties={4}",
                    depth,
                    value,
                    value.GetType().FullName,
                    DescribeFields(value),
                    DescribeProperties(value));
            }
            return;
        }
        if (!visited.Add(value))
        {
            return;
        }
        foreach (var item in enumerable)
        {
            if (item == null)
            {
                continue;
            }
            var itemType = item.GetType();
            var keyField = itemType.GetField(
                "key",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var valueField = itemType.GetField(
                "value",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (keyField != null && valueField != null)
            {
                var key = keyField.GetValue(item);
                var keyDescription = string.Format(
                    "parentType={0} key={1} keyType={2} fields={3} properties={4}",
                    value.GetType().FullName,
                    key,
                    key == null ? "<null>" : key.GetType().FullName,
                    DescribeFields(key),
                    DescribeProperties(key));
                if (ScannedKeys.Add(keyDescription))
                {
                    Console.WriteLine("KEY depth={0} {1}", depth, keyDescription);
                }
                if (Convert.ToString(key) == "text")
                {
                    var parentDictionaryField = FindField(value.GetType(), "sadF");
                    var parentDictionary = parentDictionaryField == null
                        ? null
                        : parentDictionaryField.GetValue(value) as IDictionary;
                    if (parentDictionary != null)
                    {
                        foreach (DictionaryEntry dictionaryPair in parentDictionary)
                        {
                            if (Convert.ToString(dictionaryPair.Key) != "text")
                            {
                                continue;
                            }
                            var wrapper = dictionaryPair.Value;
                            Console.WriteLine(
                                "TEXT-WRAPPER type={0} fields={1} properties={2} constructors={3}",
                                wrapper.GetType().FullName,
                                DescribeFields(wrapper),
                                DescribeProperties(wrapper),
                                string.Join(",", wrapper.GetType().GetConstructors(
                                    BindingFlags.Instance | BindingFlags.Public |
                                    BindingFlags.NonPublic)
                                    .Select(constructor => "(" + string.Join(",",
                                        constructor.GetParameters()
                                            .Select(parameter =>
                                                parameter.ParameterType.FullName)
                                            .ToArray()) + ")")
                                    .ToArray()));
                        }
                    }
                }
                var keyText = Convert.ToString(key);
                if (keyText == "dependencies" ||
                    keyText == "root-panel-infos" ||
                    keyText == "master-mode" ||
                    keyText == "mastermap")
                {
                    var packageValue = valueField.GetValue(item);
                    var collectionField = packageValue == null
                        ? null
                        : FindField(packageValue.GetType(), "caKa");
                    var collection = collectionField == null
                        ? null
                        : collectionField.GetValue(packageValue) as ICollection;
                    Console.WriteLine(
                        "PACKAGE-INFO key={0} valueType={1} count={2} fields={3}",
                        keyText,
                        packageValue == null
                            ? "<null>"
                            : packageValue.GetType().FullName,
                        collection == null ? -1 : collection.Count,
                        DescribeFields(packageValue));
                }
                if (keyText == "PropList" && _styleGraphsPrinted < 3)
                {
                    _styleGraphsPrinted++;
                    Console.WriteLine(
                        "PROP-LIST-GRAPH index={0}",
                        _styleGraphsPrinted);
                    DescribeValueGraph(
                        valueField.GetValue(item),
                        0,
                        new HashSet<object>(ReferenceComparer.Instance));
                }
                if (keyText == "selected-tab-package-id" ||
                    keyText == "selected-tab-object-id" ||
                    keyText == "open-tab-package-ids" ||
                    keyText == "open-tab-object-ids")
                {
                    var settingValue = valueField.GetValue(item);
                    Console.WriteLine(
                        "SETTING key={0} valueType={1} fields={2} properties={3}",
                        keyText,
                        settingValue == null
                            ? "<null>"
                            : settingValue.GetType().FullName,
                        DescribeFields(settingValue),
                        DescribeProperties(settingValue));
                    if (settingValue != null)
                    {
                        var collectionField = FindField(
                            settingValue.GetType(),
                            "caKa");
                        var settingItems = collectionField == null
                            ? null
                            : collectionField.GetValue(settingValue) as IEnumerable;
                        if (settingItems != null)
                        {
                            var itemIndex = 0;
                            foreach (var settingItem in settingItems)
                            {
                                Console.WriteLine(
                                    "SETTING-ITEM key={0} index={1} valueType={2} fields={3} properties={4}",
                                    keyText,
                                    itemIndex++,
                                    settingItem == null
                                        ? "<null>"
                                        : settingItem.GetType().FullName,
                                    DescribeFields(settingItem),
                                    DescribeProperties(settingItem));
                            }
                        }
                    }
                }
                if (IsRp11OnlyStyleName(Convert.ToString(key)))
                {
                    var styleValue = valueField.GetValue(item);
                    Console.WriteLine(
                        "STYLE key={0} valueType={1} fields={2} properties={3}",
                        key,
                        styleValue == null
                            ? "<null>"
                            : styleValue.GetType().FullName,
                        DescribeFields(styleValue),
                        DescribeProperties(styleValue));
                    Console.WriteLine(
                        "PARENT type={0} interfaces={1} fields={2} methods={3}",
                        value.GetType().FullName,
                        string.Join(",", value.GetType().GetInterfaces()
                            .Select(type => type.FullName).ToArray()),
                        DescribeFields(value),
                        string.Join(",", value.GetType().GetMethods(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .Where(method => method.GetParameters().Length <= 2)
                            .Select(method => method.Name + "/" + method.GetParameters().Length)
                            .Distinct()
                            .ToArray()));
                }
                if (HasUnsupportedStyleId(key))
                {
                    Console.WriteLine(
                        "MATCH depth={0} parentType={1} key={2} keyType={3} fields={4}",
                        depth,
                        value.GetType().FullName,
                        key,
                        key == null ? "<null>" : key.GetType().FullName,
                        DescribeFields(key));
                }
                ScanKeysRecursive(valueField.GetValue(item), depth + 1, visited);
            }
            else
            {
                ScanKeysRecursive(item, depth + 1, visited);
            }
        }
        visited.Remove(value);
    }

    private static bool HasUnsupportedStyleId(object key)
    {
        if (key == null)
        {
            return false;
        }
        int parsed;
        if (int.TryParse(Convert.ToString(key), out parsed) &&
            IsUnsupportedStyleId(parsed))
        {
            return true;
        }
        foreach (var field in key.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var fieldValue = field.GetValue(key);
            try
            {
                if (fieldValue != null &&
                    IsUnsupportedStyleId(Convert.ToInt32(fieldValue)))
                {
                    return true;
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static void DescribeValueGraph(
        object value,
        int depth,
        HashSet<object> visited)
    {
        if (value == null || depth > 4)
        {
            return;
        }
        Console.WriteLine(
            "GRAPH depth={0} type={1} value={2} fields={3} properties={4}",
            depth,
            value.GetType().FullName,
            Convert.ToString(value),
            DescribeFields(value),
            DescribeProperties(value));
        if (value is string || value is byte[] || !visited.Add(value))
        {
            return;
        }
        var enumerable = value as IEnumerable;
        if (enumerable != null)
        {
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= 24)
                {
                    break;
                }
                DescribeValueGraph(item, depth + 1, visited);
                if (item == null)
                {
                    continue;
                }
                foreach (var field in item.GetType().GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic))
                {
                    DescribeValueGraph(
                        field.GetValue(item),
                        depth + 2,
                        visited);
                }
            }
        }
        visited.Remove(value);
    }

    private static bool IsUnsupportedStyleId(int value)
    {
        return value == 108 || value == 109 ||
            (value >= 1400 && value <= 1405);
    }

    private static bool IsRp11OnlyStyleName(string value)
    {
        switch (value)
        {
            case "Radius":
            case "Duration":
            case "Easing":
            case "ScaleX":
            case "ScaleY":
            case "TranslateX":
            case "TranslateY":
            case "Rotate":
                return true;
            default:
                return false;
        }
    }

    private static string DescribeFields(object value)
    {
        if (value == null)
        {
            return "";
        }
        return string.Join(
            ",",
            value.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.Name + "=" + Convert.ToString(field.GetValue(value)))
                .ToArray());
    }

    private static string DescribeProperties(object value)
    {
        if (value == null)
        {
            return "";
        }
        var values = new List<string>();
        foreach (var property in value.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }
            try
            {
                values.Add(
                    property.Name + "=" +
                    Convert.ToString(property.GetValue(value, null)));
            }
            catch
            {
            }
        }
        return string.Join(",", values.ToArray());
    }

    private static void DumpHeader(string path)
    {
        var source = File.ReadAllBytes(path);
        if (source.Length < 12 || source[0] != 0xAC || source[1] != 0xEF)
        {
            throw new InvalidDataException("Input is not an Axure RP container.");
        }
        var headerLength = BitConverter.ToInt32(source, 4);
        if (headerLength < 1 || headerLength > source.Length - 8)
        {
            throw new InvalidDataException("Invalid Axure RP header length.");
        }
        var compressedHeader = new byte[headerLength];
        Buffer.BlockCopy(source, 8, compressedHeader, 0, headerLength);
        Console.WriteLine(Encoding.UTF8.GetString(DecodeLz4(compressedHeader)));
    }

    private static void Rewrite(
        string sourcePath,
        string outputPath)
    {
        ReportProgress(22, "read_source");
        var source = File.ReadAllBytes(sourcePath);
        if (source.Length < 12 || source[0] != 0xAC || source[1] != 0xEF)
        {
            throw new InvalidDataException("Source is not an Axure RP container.");
        }

        var headerLength = BitConverter.ToInt32(source, 4);
        var compressedHeader = new byte[headerLength];
        Buffer.BlockCopy(source, 8, compressedHeader, 0, headerLength);
        var headerJson = Encoding.UTF8.GetString(DecodeLz4(compressedHeader));

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var header = serializer.Deserialize<Dictionary<string, object>>(headerJson);
        ReportProgress(25, "rewrite_version_metadata");
        RenameVersionPart(header);

        var parts = new List<Part>();
        CollectParts(header, parts);
        parts.Sort((left, right) => left.Offset.CompareTo(right.Offset));

        var dataBase = 8 + headerLength + 4;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var partProgress = 28 + (parts.Count == 0
                ? 0
                : (partIndex * 56 / parts.Count));
            ReportProgress(partProgress, "scan_package");
            var recordOffset = dataBase + part.Offset;
            var gzipLength = BitConverter.ToInt32(source, recordOffset);
            part.Gzip = new byte[gzipLength];
            Buffer.BlockCopy(source, recordOffset + 4, part.Gzip, 0, gzipLength);
            if (part.Key == Rp9VersionPartName)
            {
                part.Gzip = Gzip(Encoding.UTF8.GetBytes(Rp9BreakingChanges));
                _gzipParts++;
                continue;
            }

            if (part.Gzip.Length < 3 ||
                part.Gzip[0] != 0x1F ||
                part.Gzip[1] != 0x8B ||
                part.Gzip[2] != 0x08)
            {
                continue;
            }

            _gzipParts++;
            var decoded = Gunzip(part.Gzip);
            if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:DesignDocument")))
            {
                ReportProgress(partProgress, "rewrite_design_document");
                part.Gzip = Gzip(RewriteObjectPackage(decoded));
                _designDocumentsRewritten++;
            }
            else if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:DocumentSettings")))
            {
                ReportProgress(partProgress, "rewrite_document_settings");
                part.Gzip = Gzip(RewriteObjectPackage(decoded));
                _settingsRewritten++;
            }
            else if (Contains(decoded, Encoding.ASCII.GetBytes("Axure:Page")))
            {
                ReportProgress(partProgress, "rewrite_page_and_widgets");
                bool containsPageRecord;
                part.Gzip = Gzip(RewriteObjectPackage(
                    decoded,
                    out containsPageRecord));
                if (containsPageRecord)
                {
                    _pagesRewritten++;
                }
                else
                {
                    _objectPackagesRewritten++;
                }
            }
        }

        ReportProgress(86, "rebuild_package_index");
        var nextOffset = 0;
        foreach (var part in parts)
        {
            part.Offset = nextOffset;
            part.Parent[part.Key] = nextOffset;
            nextOffset += 4 + part.Gzip.Length + 4;
        }

        var rewrittenJson = serializer.Serialize(header);
        var rewrittenHeader = EncodeLz4(Encoding.UTF8.GetBytes(rewrittenJson));

        ReportProgress(92, "write_rp9_file");
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
        {
            writer.Write((byte)0xAC);
            writer.Write((byte)0xEF);
            writer.Write((ushort)9);
            writer.Write(rewrittenHeader.Length);
            writer.Write(rewrittenHeader);
            writer.Write(0);
            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                writer.Write(part.Gzip.Length);
                writer.Write(part.Gzip);
                if (index + 1 < parts.Count)
                {
                    writer.Write(0);
                }
            }
            writer.Flush();
            File.WriteAllBytes(outputPath, output.ToArray());
            ReportProgress(95, "bridge_complete");
            Console.WriteLine(serializer.Serialize(new Dictionary<string, object>
            {
                { "status", "success" },
                { "parts", parts.Count },
                { "gzipParts", _gzipParts },
                { "pagesRewritten", _pagesRewritten },
                { "objectPackagesRewritten", _objectPackagesRewritten },
                { "designDocumentsRewritten", _designDocumentsRewritten },
                { "settingsRewritten", _settingsRewritten },
                { "interactionsRemoved", 0 },
                { "unsupportedStylePropertiesRemoved", _unsupportedStylePropertiesRemoved },
                { "rp9RequiredFieldsAdded", _rp9RequiredFieldsAdded },
                { "unsupportedWorkspaceTabsRemoved", _unsupportedWorkspaceTabsRemoved },
                { "unsupportedSettingsPropertiesRemoved", _unsupportedSettingsPropertiesRemoved },
                { "staticRecordsVerified", _staticRecordsVerified },
                { "staticScalarsVerified", _staticScalarsVerified },
                { "headerBytes", rewrittenHeader.Length },
                { "outputBytes", output.Length }
            }));
        }
    }

    private static void ReportProgress(int percent, string stage)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        if (percent == _lastProgress &&
            string.Equals(stage, _lastProgressStage, StringComparison.Ordinal))
        {
            return;
        }
        _lastProgress = percent;
        _lastProgressStage = stage;
        Console.Error.WriteLine(
            "PROGRESS\t{0}\t{1}",
            percent,
            stage);
        Console.Error.Flush();
    }

    private static void RenameVersionPart(Dictionary<string, object> node)
    {
        foreach (var value in node.Values.ToArray())
        {
            var child = value as Dictionary<string, object>;
            if (child != null)
            {
                RenameVersionPart(child);
            }
        }

        var versionKey = node.Keys.FirstOrDefault(key => key.EndsWith(".version"));
        if (versionKey == null || versionKey == Rp9VersionPartName)
        {
            return;
        }
        var offset = node[versionKey];
        node.Remove(versionKey);
        node[Rp9VersionPartName] = offset;
    }

    private static void CollectParts(
        Dictionary<string, object> node,
        List<Part> result)
    {
        foreach (var pair in node)
        {
            var child = pair.Value as Dictionary<string, object>;
            if (child != null)
            {
                CollectParts(child, result);
                continue;
            }
            if (pair.Value is int)
            {
                result.Add(new Part
                {
                    Parent = node,
                    Key = pair.Key,
                    Offset = (int)pair.Value
                });
            }
        }
    }

    private static byte[] Gzip(byte[] value)
    {
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(
                output,
                CompressionMode.Compress,
                leaveOpen: true))
            {
                gzip.Write(value, 0, value.Length);
            }
            return output.ToArray();
        }
    }

    private static void InitializeAxureWriter()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(_axureDirectory, "AxureRP9.exe"));
        var serializerType = assembly.GetType("Pacj.jac4", throwOnError: true);
        var singletonMethod = serializerType.GetMethod(
            "S4hl",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _serializer = singletonMethod.Invoke(null, null);
        _loadMethod = serializerType.Module.ResolveMethod(LoadMethodToken) as MethodInfo;
        _saveMethod = serializerType.Module.ResolveMethod(SaveMethodToken) as MethodInfo;
        serializerType.Module
            .ResolveField(UseMemoryStreamFieldToken)
            .SetValue(null, true);

        var versionType = assembly.GetType("KpkI.MpkP", throwOnError: true);
        var versionConstructor = versionType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First(constructor => constructor.GetParameters().Length == 4);
        _targetVersion = versionConstructor.Invoke(new object[] { 9, 0, 1, 0 });
    }

    private static byte[] RewriteObjectPackage(byte[] decoded)
    {
        bool ignored;
        return RewriteObjectPackage(decoded, out ignored);
    }

    private static byte[] RewriteObjectPackage(
        byte[] decoded,
        out bool containsPageRecord)
    {
        object packageContext;
        using (var input = new MemoryStream(decoded, writable: false))
        {
            packageContext = _loadMethod.Invoke(
                _serializer,
                new object[] { input, 96.0, false });
        }
        containsPageRecord = ContainsRecordType(
            packageContext,
            "Axure:Page");

        var versionField = packageContext.GetType().GetField(
            "qatb",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        versionField.SetValue(packageContext, _targetVersion);
        var requiredMasterFields =
            CaptureRequiredMasterFields(packageContext);
        // Interaction tree maps are structural dependencies of pages and
        // masters in RP9. Removing their records leaves master references
        // dangling and can crash the RP9 editor while opening the document.
        // Keep the serializer-owned scaffolding intact; unsupported visual
        // data is still stripped independently below.
        _unsupportedStylePropertiesRemoved +=
            StripUnsupportedStyleProperties(packageContext);
        _rp9RequiredFieldsAdded += EnsureRp9PackageInfoFields(packageContext);
        _rp9RequiredFieldsAdded +=
            RestoreRequiredMasterFields(requiredMasterFields);
        _unsupportedWorkspaceTabsRemoved +=
            StripUnsupportedWorkspaceTabs(packageContext);
        _unsupportedSettingsPropertiesRemoved +=
            StripUnsupportedSettingsProperties(packageContext);
        var staticBefore = CaptureStaticSnapshot(packageContext);

        using (var output = new MemoryStream())
        {
            _saveMethod.Invoke(_serializer, new[] { packageContext, output });
            var rewritten = output.ToArray();
            object reloaded;
            using (var verificationInput = new MemoryStream(rewritten, writable: false))
            {
                reloaded = _loadMethod.Invoke(
                    _serializer,
                    new object[] { verificationInput, 96.0, false });
            }
            ValidateRp9RequiredFields(reloaded);
            var staticAfter = CaptureStaticSnapshot(reloaded);
            if (!staticBefore.Signatures.SequenceEqual(staticAfter.Signatures))
            {
                throw new InvalidDataException(
                    "Static object verification failed after RP9 serialization.");
            }
            _staticRecordsVerified += staticAfter.RecordCount;
            _staticScalarsVerified += staticAfter.ScalarCount;
            return rewritten;
        }
    }

    private static bool ContainsRecordType(
        object packageContext,
        string requestedType)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return false;
        }
        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (typeNameField != null &&
                string.Equals(
                    Convert.ToString(typeNameField.GetValue(item)),
                    requestedType,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static int EnsureRp9PackageInfoFields(object packageContext)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return 0;
        }

        var added = 0;
        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (typeNameField == null)
            {
                continue;
            }
            var recordType = Convert.ToString(typeNameField.GetValue(item));
            if (recordType != "Axure:PackageInfo" &&
                recordType != "Axure:MasterPackageInfo")
            {
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

            object collectionWrapperTemplate = null;
            var hasRootPanelInfos = false;
            foreach (DictionaryEntry pair in dictionary)
            {
                var key = Convert.ToString(pair.Key);
                if (key == "root-panel-infos")
                {
                    hasRootPanelInfos = true;
                    break;
                }
                if (key == "dependencies")
                {
                    collectionWrapperTemplate = pair.Value;
                }
            }
            if (hasRootPanelInfos || collectionWrapperTemplate == null)
            {
                continue;
            }

            var emptyCollectionWrapper = Activator.CreateInstance(
                collectionWrapperTemplate.GetType(),
                nonPublic: true);
            var addMethod = item.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "Add")
                    {
                        return false;
                    }
                    var parameters = method.GetParameters();
                    return parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType.IsInstanceOfType(
                            emptyCollectionWrapper);
                });
            if (addMethod == null)
            {
                throw new InvalidDataException(
                    "Could not add the RP9 root-panel-infos package field.");
            }
            addMethod.Invoke(
                item,
                new[] { (object)"root-panel-infos", emptyCollectionWrapper });
            added++;
        }
        return added;
    }

    private static void ValidateRp9RequiredFields(object packageContext)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            throw new InvalidDataException(
                "RP9 package verification could not enumerate records.");
        }

        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            var recordType = typeNameField == null
                ? ""
                : Convert.ToString(typeNameField.GetValue(item));
            var dictionaryField = FindField(item.GetType(), "sadF");
            var dictionary = dictionaryField == null
                ? null
                : dictionaryField.GetValue(item) as IDictionary;
            if (dictionary == null)
            {
                continue;
            }

            if (recordType == "Axure:PackageInfo" ||
                recordType == "Axure:MasterPackageInfo")
            {
                var rootPanelInfos = FindDictionaryValue(
                    dictionary,
                    "root-panel-infos");
                if (rootPanelInfos == null)
                {
                    throw new InvalidDataException(
                        recordType +
                        " is missing the RP9 root-panel-infos field.");
                }
                var collectionField = FindField(
                    rootPanelInfos.GetType(),
                    "caKa");
                var entries = collectionField == null
                    ? null
                    : collectionField.GetValue(rootPanelInfos) as IEnumerable;
                if (entries == null)
                {
                    throw new InvalidDataException(
                        recordType +
                        " has an invalid root-panel-infos collection.");
                }
                foreach (var entry in entries)
                {
                    if (entry != null &&
                        FindField(entry.GetType(), "caKa") != null)
                    {
                        throw new InvalidDataException(
                            recordType +
                            " contains a nested collection where RP9 " +
                            "expects a root-panel object reference.");
                    }
                }
            }

            if (recordType == "Axure:MasterPackageInfo" &&
                FindDictionaryValue(dictionary, "master-mode") == null)
            {
                throw new InvalidDataException(
                    "Axure:MasterPackageInfo is missing master-mode.");
            }
            if (recordType == "Axure:DesignDocument" &&
                FindDictionaryValue(dictionary, "mastermap") == null)
            {
                throw new InvalidDataException(
                    "Axure:DesignDocument is missing mastermap.");
            }
            if (recordType == "Axure:PrintSettings")
            {
                foreach (var requiredKey in new[]
                {
                    "landscape",
                    "margin-bottom",
                    "margin-left",
                    "margin-right",
                    "margin-top"
                })
                {
                    if (FindDictionaryValue(dictionary, requiredKey) == null)
                    {
                        throw new InvalidDataException(
                            "Axure:PrintSettings is missing " +
                            requiredKey + ".");
                    }
                }
            }
        }
    }

    private static object FindDictionaryValue(
        IDictionary dictionary,
        string requestedKey)
    {
        foreach (DictionaryEntry pair in dictionary)
        {
            if (Convert.ToString(pair.Key) == requestedKey)
            {
                return pair.Value;
            }
        }
        return null;
    }

    private sealed class RequiredMasterField
    {
        public object Record;
        public string Key;
        public object Value;
    }

    private static List<RequiredMasterField> CaptureRequiredMasterFields(
        object packageContext)
    {
        var captured = new List<RequiredMasterField>();
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return captured;
        }

        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            var recordType = typeNameField == null
                ? ""
                : Convert.ToString(typeNameField.GetValue(item));
            string requiredKey;
            if (recordType == "Axure:MasterPackageInfo")
            {
                requiredKey = "master-mode";
            }
            else if (recordType == "Axure:DesignDocument")
            {
                requiredKey = "mastermap";
            }
            else
            {
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

            foreach (DictionaryEntry pair in dictionary)
            {
                if (Convert.ToString(pair.Key) == requiredKey)
                {
                    captured.Add(new RequiredMasterField
                    {
                        Record = item,
                        Key = requiredKey,
                        Value = pair.Value
                    });
                    break;
                }
            }
        }
        return captured;
    }

    private static int RestoreRequiredMasterFields(
        IEnumerable<RequiredMasterField> captured)
    {
        var restored = 0;
        foreach (var field in captured)
        {
            if (field.Record == null || field.Value == null)
            {
                continue;
            }

            var dictionaryField = FindField(
                field.Record.GetType(),
                "sadF");
            var dictionary = dictionaryField == null
                ? null
                : dictionaryField.GetValue(field.Record) as IDictionary;
            if (dictionary == null)
            {
                continue;
            }
            object currentKey = null;
            foreach (DictionaryEntry pair in dictionary)
            {
                if (Convert.ToString(pair.Key) == field.Key)
                {
                    currentKey = pair.Key;
                    break;
                }
            }
            if (currentKey != null)
            {
                dictionary.Remove(currentKey);
            }

            var addMethod = field.Record.GetType().GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "Add")
                    {
                        return false;
                    }
                    var parameters = method.GetParameters();
                    return parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType.IsInstanceOfType(
                            field.Value);
                });
            if (addMethod == null)
            {
                throw new InvalidDataException(
                    "Could not refresh the RP9 required field '" +
                    field.Key + "'.");
            }

            addMethod.Invoke(
                field.Record,
                new[] { (object)field.Key, field.Value });
            restored++;
        }
        return restored;
    }

    private static int StripUnsupportedWorkspaceTabs(object packageContext)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return 0;
        }

        var removed = 0;
        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (typeNameField == null ||
                Convert.ToString(typeNameField.GetValue(item)) !=
                    "Axure:DocumentSettings")
            {
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

            IList packageIds = null;
            IList objectIds = null;
            foreach (DictionaryEntry pair in dictionary)
            {
                var key = Convert.ToString(pair.Key);
                if (key != "open-tab-package-ids" &&
                    key != "open-tab-object-ids")
                {
                    continue;
                }
                var collectionField = pair.Value == null
                    ? null
                    : FindField(pair.Value.GetType(), "caKa");
                var collection = collectionField == null
                    ? null
                    : collectionField.GetValue(pair.Value) as IList;
                if (key == "open-tab-package-ids")
                {
                    packageIds = collection;
                }
                else
                {
                    objectIds = collection;
                }
            }
            if (packageIds == null)
            {
                continue;
            }

            for (var index = packageIds.Count - 1; index >= 0; index--)
            {
                var guid = ReadGuidWrapper(packageIds[index]);
                if (!guid.HasValue || guid.Value != Guid.Empty)
                {
                    continue;
                }
                packageIds.RemoveAt(index);
                if (objectIds != null && index < objectIds.Count)
                {
                    objectIds.RemoveAt(index);
                }
                removed++;
            }
        }
        return removed;
    }

    private static int StripUnsupportedSettingsProperties(
        object packageContext)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IEnumerable;
        if (objects == null)
        {
            return 0;
        }

        var removed = 0;
        foreach (var item in objects)
        {
            if (item == null)
            {
                continue;
            }
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (typeNameField == null ||
                Convert.ToString(typeNameField.GetValue(item)) !=
                    "Axure:DocumentSettings")
            {
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
                switch (Convert.ToString(pair.Key))
                {
                    case "FloatingEditorLayoutInfos":
                    case "PrototypeDeleted":
                    case "ShowLastPublishedCurrentLinks":
                    case "UploadedSitmapIds":
                        keysToRemove.Add(pair.Key);
                        break;
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

    private static Guid? ReadGuidWrapper(object value)
    {
        if (value == null)
        {
            return null;
        }
        var guidField = FindField(value.GetType(), "EaDI");
        if (guidField == null)
        {
            return null;
        }
        var fieldValue = guidField.GetValue(value);
        if (fieldValue is Guid)
        {
            return (Guid)fieldValue;
        }
        Guid parsed;
        return Guid.TryParse(Convert.ToString(fieldValue), out parsed)
            ? parsed
            : (Guid?)null;
    }

    private static int StripUnsupportedStyleProperties(object packageContext)
    {
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext);
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        return StripUnsupportedStylePropertiesRecursive(
            objects,
            0,
            visited,
            "");
    }

    private static int StripUnsupportedStylePropertiesRecursive(
        object value,
        int depth,
        HashSet<object> visited,
        string containingRecordType)
    {
        if (value == null || value is string || value is byte[] || depth > 32)
        {
            return 0;
        }
        if (!visited.Add(value))
        {
            return 0;
        }

        var recordType = containingRecordType;
        var typeNameField = FindField(value.GetType(), "Qaup");
        if (typeNameField != null)
        {
            var candidateRecordType =
                Convert.ToString(typeNameField.GetValue(value));
            if (!string.IsNullOrEmpty(candidateRecordType))
            {
                recordType = candidateRecordType;
            }
        }

        var removed = 0;
        var dictionaryField = FindField(value.GetType(), "sadF");
        var dictionary = dictionaryField == null
            ? null
            : dictionaryField.GetValue(value) as IDictionary;
        if (dictionary != null)
        {
            var keysToRemove = new List<object>();
            foreach (DictionaryEntry pair in dictionary)
            {
                var keyName = Convert.ToString(pair.Key);
                if (keyName == "PropList")
                {
                    removed += StripUnsupportedPropList(pair.Value);
                }
                if (HasUnsupportedStyleId(pair.Key) &&
                    Environment.GetEnvironmentVariable(
                        "AXURE_TRACE_STYLE_CANDIDATES") == "1")
                {
                    Console.Error.WriteLine(
                        "STYLE-CANDIDATE recordType={0} key={1} parentType={2}",
                        recordType,
                        keyName,
                        value.GetType().FullName);
                }
                // Numeric DOA key identifiers are schema-local. The same
                // number can represent a style property on a diagram object
                // and a required document or print-setting field elsewhere.
                // Restrict direct numeric cleanup to visual widget records;
                // PropList entries are handled independently above.
                var isDiagramObject =
                    recordType.StartsWith(
                        "Axure:DiagramObject:",
                        StringComparison.Ordinal);
                if (IsRp11OnlyStyleName(keyName) ||
                    (isDiagramObject && HasUnsupportedStyleId(pair.Key)))
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

        var enumerable = value as IEnumerable;
        if (enumerable != null)
        {
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }
                var itemType = item.GetType();
                var valueField = itemType.GetField(
                    "value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                removed += StripUnsupportedStylePropertiesRecursive(
                    valueField == null ? item : valueField.GetValue(item),
                    depth + 1,
                    visited,
                    recordType);
            }
        }
        visited.Remove(value);
        return removed;
    }

    private static int StripUnsupportedPropList(object propList)
    {
        if (propList == null)
        {
            return 0;
        }
        var collectionField = FindField(propList.GetType(), "caKa");
        var items = collectionField == null
            ? null
            : collectionField.GetValue(propList) as IList;
        if (items == null)
        {
            return 0;
        }

        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            var item = items[index];
            var dictionaryField = item == null
                ? null
                : FindField(item.GetType(), "sadF");
            var dictionary = dictionaryField == null
                ? null
                : dictionaryField.GetValue(item) as IDictionary;
            if (dictionary == null)
            {
                continue;
            }

            int? encodedName = null;
            foreach (DictionaryEntry pair in dictionary)
            {
                if (Convert.ToString(pair.Key) != "DOAName" ||
                    pair.Value == null)
                {
                    continue;
                }
                var integerField = FindField(pair.Value.GetType(), "Xaqk");
                if (integerField != null)
                {
                    encodedName = Convert.ToInt32(
                        integerField.GetValue(pair.Value));
                }
                break;
            }
            if (!encodedName.HasValue)
            {
                continue;
            }

            var styleId = encodedName.Value & 0x3FFFFFFF;
            if (!IsUnsupportedStyleId(styleId))
            {
                continue;
            }
            items.RemoveAt(index);
            removed++;
        }
        return removed;
    }

    private static bool IsRp9StructuralKey(string key)
    {
        switch (key)
        {
            case "interactionmap":
            case "root-panel-infos":
            case "dependencies":
            case "sitemap":
            case "tree":
            case "node-table":
                return true;
            default:
                return false;
        }
    }

    private sealed class StaticSnapshot
    {
        public string[] Signatures;
        public int RecordCount;
        public int ScalarCount;
    }

    private static StaticSnapshot CaptureStaticSnapshot(object packageContext)
    {
        var signatures = new List<string>();
        var scalarCount = 0;
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IList;
        if (objects == null)
        {
            return new StaticSnapshot
            {
                Signatures = new string[0],
                RecordCount = 0,
                ScalarCount = 0
            };
        }

        foreach (var item in objects)
        {
            var typeNameField = item.GetType().GetField(
                "Qaup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var typeName = typeNameField == null
                ? ""
                : Convert.ToString(typeNameField.GetValue(item));
            if (IsInteractionType(typeName))
            {
                continue;
            }

            var visited = new HashSet<object>(ReferenceComparer.Instance);
            signatures.Add(
                typeName + "\u001f" +
                CanonicalizeStaticValue(item, 0, visited, ref scalarCount));
        }

        signatures.Sort(StringComparer.Ordinal);
        return new StaticSnapshot
        {
            Signatures = signatures.ToArray(),
            RecordCount = signatures.Count,
            ScalarCount = scalarCount
        };
    }

    private static string CanonicalizeStaticValue(
        object value,
        int depth,
        HashSet<object> visited,
        ref int scalarCount)
    {
        if (value == null)
        {
            return "<null>";
        }
        if (depth > 24)
        {
            return "<max-depth>";
        }
        var bytes = value as byte[];
        if (bytes != null)
        {
            scalarCount++;
            using (var sha256 = SHA256.Create())
            {
                return "bytes:" + bytes.Length + ":" +
                    Convert.ToBase64String(sha256.ComputeHash(bytes));
            }
        }

        var type = value.GetType();
        if (type.IsEnum || type.IsPrimitive ||
            value is string || value is decimal ||
            value is Guid || value is DateTime)
        {
            scalarCount++;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        var enumerable = value as IEnumerable;
        if (enumerable != null)
        {
            if (!visited.Add(value))
            {
                return "<cycle>";
            }
            var values = new List<string>();
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    values.Add("<null>");
                    continue;
                }
                var itemType = item.GetType();
                var keyField = itemType.GetField(
                    "key",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var valueField = itemType.GetField(
                    "value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (keyField != null && valueField != null)
                {
                    var key = Convert.ToString(keyField.GetValue(item));
                    if (key.IndexOf("interaction", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }
                    values.Add(
                        key + "=" +
                        CanonicalizeStaticValue(
                            valueField.GetValue(item),
                            depth + 1,
                            visited,
                            ref scalarCount));
                }
                else
                {
                    values.Add(CanonicalizeStaticValue(
                        item,
                        depth + 1,
                        visited,
                        ref scalarCount));
                }
            }
            visited.Remove(value);
            values.Sort(StringComparer.Ordinal);
            return "[" + string.Join("\u001e", values) + "]";
        }

        scalarCount++;
        return type.FullName + ":" +
            Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object left, object right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }

    private static int StripInteractions(object packageContext)
    {
        var removed = 0;
        var objectsField = packageContext.GetType().GetField(
            "yatu",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var objects = objectsField == null
            ? null
            : objectsField.GetValue(packageContext) as IList;
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
            if (IsInteractionType(typeName))
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

    private static bool IsInteractionType(string typeName)
    {
        return typeName.IndexOf(
                "Interaction",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf(
                "Interation",
                StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static byte[] Gunzip(byte[] value)
    {
        using (var input = new MemoryStream(value, writable: false))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
        {
            var matches = true;
            for (var index = 0; index < needle.Length; index++)
            {
                if (haystack[offset + index] != needle[index])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsAsciiToken(
        byte[] haystack,
        byte[] token)
    {
        for (var offset = 0; offset <= haystack.Length - token.Length; offset++)
        {
            var matches = true;
            for (var index = 0; index < token.Length; index++)
            {
                if (haystack[offset + index] != token[index])
                {
                    matches = false;
                    break;
                }
            }
            if (!matches)
            {
                continue;
            }
            var nextOffset = offset + token.Length;
            if (nextOffset >= haystack.Length)
            {
                return true;
            }
            var next = haystack[nextOffset];
            var continuesIdentifier =
                (next >= (byte)'A' && next <= (byte)'Z') ||
                (next >= (byte)'a' && next <= (byte)'z') ||
                (next >= (byte)'0' && next <= (byte)'9') ||
                next == (byte)'_' ||
                next == (byte)':';
            if (!continuesIdentifier)
            {
                return true;
            }
        }
        return false;
    }

    private static byte[] DecodeLz4(byte[] value)
    {
        using (var input = new MemoryStream(value, writable: false))
        using (var decoder = LZ4Legacy.Decode(input, leaveOpen: false))
        using (var output = new MemoryStream())
        {
            decoder.CopyTo(output);
            return output.ToArray();
        }
    }

    private static byte[] EncodeLz4(byte[] value)
    {
        using (var output = new MemoryStream())
        {
            using (var encoder = LZ4Legacy.Encode(
                output,
                highCompression: true,
                blockSize: 1024 * 1024,
                leaveOpen: true))
            {
                encoder.Write(value, 0, value.Length);
            }
            return output.ToArray();
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
}
