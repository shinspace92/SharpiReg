using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Principal;
// using Microsoft.Win32; Win32 RegistryXXX seems to be using the functions in advapi32.dll. Prepending the Registry values with null bytes or
// Other "invisible" unicode characters don't seem to do the trick. Use NtRegistryXXX functions from ntdll instead.

class SharpiReg
{
    const uint KEY_ALL_ACCESS = 0xF003F;
    const uint OBJ_CASE_INSENSITIVE = 0x40;
    const uint REG_SZ = 1;
    const int STATUS_NO_MORE_ENTRIES = unchecked((int)0x8000001A);

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEY_VALUE_BASIC_INFORMATION
    {
        public uint TitleIndex;
        public uint Type;
        public uint NameLength;
        // WCHAR Name[1] follows
    }

    [DllImport("ntdll.dll")]
    public static extern int NtCreateKey(out IntPtr KeyHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, int TitleIndex, IntPtr Class, uint CreateOptions, out uint Disposition);

    [DllImport("ntdll.dll")]
    public static extern int NtSetValueKey(IntPtr KeyHandle, ref UNICODE_STRING ValueName, uint TitleIndex, uint Type, IntPtr Data, uint DataSize);

    [DllImport("ntdll.dll")]
    public static extern int NtEnumerateValueKey(IntPtr KeyHandle, uint Index, int KeyValueInformationClass, IntPtr KeyValueInformation, uint Length, out uint ResultLength);

    [DllImport("ntdll.dll")]
    public static extern int NtDeleteValueKey(IntPtr KeyHandle, ref UNICODE_STRING ValueName);

    [DllImport("ntdll.dll")]
    public static extern int NtClose(IntPtr Handle);

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        string command = args[0].ToLower();

        try
        {
            if (command == "create" && args.Length == 4)
            {
                CreateValue(args[1], args[2], args[3]);
            }
            else if (command == "list" && args.Length == 2)
            {
                ListValues(args[1]);
            }
            else if (command == "delete-index" && args.Length == 3 && uint.TryParse(args[2], out uint delIndex))
            {
                DeleteValueByIndex(args[1], delIndex);
            }
            else
            {
                ShowUsage();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error: {ex.Message}");
        }
    }

    static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  SharpiReg.exe create <regPath> <valueName> <valueData>");
        Console.WriteLine("  SharpiReg.exe list <regPath>");
        Console.WriteLine("  SharpiReg.exe delete-index <regPath> <index>");
        Console.WriteLine("  regPath must begin with HKLM\\ or HKCU\\");
    }

    static void CreateValue(string regPathInput, string valueName, string valueData)
    {
        string ntPath = ConvertToNtPath(regPathInput);
        IntPtr hKey = OpenOrCreateKey(ntPath);
        UNICODE_STRING valueNameStr = StringToUnicodeStringWithLeadingNull(valueName);
        byte[] dataBytes = Encoding.Unicode.GetBytes(valueData);
        IntPtr dataPtr = Marshal.AllocHGlobal(dataBytes.Length);
        Marshal.Copy(dataBytes, 0, dataPtr, dataBytes.Length);
        NtSetValueKey(hKey, ref valueNameStr, 0, REG_SZ, dataPtr, (uint)dataBytes.Length);
        Console.WriteLine("[+] Value created with leading null byte in name");
        NtClose(hKey);
        Marshal.FreeHGlobal(dataPtr);
    }

    static void ListValues(string regPathInput)
    {
        string ntPath = ConvertToNtPath(regPathInput);
        IntPtr hKey = OpenOrCreateKey(ntPath);
        uint index = 0;
        uint bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        while (true)
        {
            int status = NtEnumerateValueKey(hKey, index, 0, buffer, bufferSize, out uint resultLen);
            if (status == STATUS_NO_MORE_ENTRIES) break;
            if (status != 0)
            {
                Console.WriteLine($"[!] NtEnumerateValueKey failed: 0x{status:X}");
                break;
            }
            KEY_VALUE_BASIC_INFORMATION info = Marshal.PtrToStructure<KEY_VALUE_BASIC_INFORMATION>(buffer);
            IntPtr namePtr = buffer + Marshal.OffsetOf<KEY_VALUE_BASIC_INFORMATION>("NameLength").ToInt32() + 4;
            string name = Marshal.PtrToStringUni(namePtr, (int)(info.NameLength / 2));
            Console.WriteLine($"[{index}] Name: {EscapeControlChars(name)} (Type={info.Type})");
            index++;
        }
        Marshal.FreeHGlobal(buffer);
        NtClose(hKey);
    }

    static void DeleteValueByIndex(string regPathInput, uint index)
    {
        string ntPath = ConvertToNtPath(regPathInput);
        IntPtr hKey = OpenOrCreateKey(ntPath);
        uint bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        int status = NtEnumerateValueKey(hKey, index, 0, buffer, bufferSize, out uint _);
        if (status != 0)
        {
            Console.WriteLine($"[!] Cannot read index {index}: 0x{status:X}");
            return;
        }
        KEY_VALUE_BASIC_INFORMATION info = Marshal.PtrToStructure<KEY_VALUE_BASIC_INFORMATION>(buffer);
        IntPtr namePtr = buffer + Marshal.OffsetOf<KEY_VALUE_BASIC_INFORMATION>("NameLength").ToInt32() + 4;
        string name = Marshal.PtrToStringUni(namePtr, (int)(info.NameLength / 2));
        UNICODE_STRING valName = StringToUnicodeString(name);
        NtDeleteValueKey(hKey, ref valName);
        Console.WriteLine($"[+] Deleted value at index {index}: {EscapeControlChars(name)}");
        Marshal.FreeHGlobal(buffer);
        NtClose(hKey);
    }

    static string ConvertToNtPath(string userPath)
    {
        if (userPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return "\\Registry\\Machine\\" + userPath.Substring(5);
        if (userPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            return $"\\Registry\\User\\{sid}\\" + userPath.Substring(5);
        }
        throw new ArgumentException("Unsupported hive. Use HKLM or HKCU.");
    }

    static IntPtr OpenOrCreateKey(string ntPath)
    {
        UNICODE_STRING ntPathStr = StringToUnicodeString(ntPath);
        IntPtr pNtPathStr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));
        Marshal.StructureToPtr(ntPathStr, pNtPathStr, false);
        OBJECT_ATTRIBUTES objAttr = new OBJECT_ATTRIBUTES
        {
            Length = Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES)),
            RootDirectory = IntPtr.Zero,
            ObjectName = pNtPathStr,
            Attributes = OBJ_CASE_INSENSITIVE,
            SecurityDescriptor = IntPtr.Zero,
            SecurityQualityOfService = IntPtr.Zero
        };
        IntPtr hKey;
        NtCreateKey(out hKey, KEY_ALL_ACCESS, ref objAttr, 0, IntPtr.Zero, 0, out _);
        Marshal.FreeHGlobal(ntPathStr.Buffer);
        Marshal.FreeHGlobal(pNtPathStr);
        return hKey;
    }

    static UNICODE_STRING StringToUnicodeString(string s)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(s);
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return new UNICODE_STRING
        {
            Length = (ushort)bytes.Length,
            MaximumLength = (ushort)bytes.Length,
            Buffer = buffer
        };
    }

    static UNICODE_STRING StringToUnicodeStringWithLeadingNull(string s)
    {
        byte[] strBytes = Encoding.Unicode.GetBytes(s);
        byte[] fullBytes = new byte[strBytes.Length + 2];
        Buffer.BlockCopy(strBytes, 0, fullBytes, 2, strBytes.Length);
        IntPtr buffer = Marshal.AllocHGlobal(fullBytes.Length);
        Marshal.Copy(fullBytes, 0, buffer, fullBytes.Length);
        return new UNICODE_STRING
        {
            Length = (ushort)fullBytes.Length,
            MaximumLength = (ushort)fullBytes.Length,
            Buffer = buffer
        };
    }

    static string EscapeControlChars(string input)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in input)
        {
            if (char.IsControl(c) || c == '\\')
                sb.Append($"\\x{(int)c:X2}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
