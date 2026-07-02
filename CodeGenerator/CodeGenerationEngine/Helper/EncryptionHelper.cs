using System.Security.Cryptography;
using System.Text;

public static class FileEncryption
{
    private static readonly byte[] Key =
        Encoding.UTF8.GetBytes("12345678901234567890123456789012");

    private static readonly byte[] IV =
        Encoding.UTF8.GetBytes("1234567890123456");

    public static void AppendEncryptedLine(string filePath, string text)
    {
        string encrypted = Encrypt(text);
        File.AppendAllText(filePath, encrypted + Environment.NewLine);
    }

    public static List<string> ReadDecryptedLines(string filePath)
    {
        var result = new List<string>();

        if (!File.Exists(filePath))
            return result;

        foreach (var line in File.ReadLines(filePath))
        {
            if (!string.IsNullOrWhiteSpace(line))
                result.Add(Decrypt(line));
        }

        return result;
    }

    private static string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = IV;

        using var ms = new MemoryStream();

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string Decrypt(string cipherText)
    {
        byte[] buffer = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = IV;

        using var ms = new MemoryStream(buffer);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);

        return sr.ReadToEnd();
    }
}
