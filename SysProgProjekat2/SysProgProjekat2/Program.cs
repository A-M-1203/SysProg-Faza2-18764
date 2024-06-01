using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

class Program
{
    private static readonly ConcurrentDictionary<string, byte[]> fileCache = new ConcurrentDictionary<string, byte[]>();
    private static readonly string secretKey = "hYT8XZ9hixqkew3OYm6vfyh7c3bGd1R5";
    private static readonly string iv = "5mFV+vX5LIOJ6oKk";

    static async Task Main(string[] args)
    {
        TcpListener server = new TcpListener(IPAddress.Any, 8080);
        server.Start();
        Console.WriteLine("Listening...");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                string request = await reader.ReadLineAsync();
                if (request.StartsWith("GET"))
                {
                    Console.WriteLine($"Request: {request}");
                    string[] tokens = request.Split(' ');
                    string fileName = tokens[1].TrimStart('/');

                    if (fileName.Contains("_enc"))
                    {
                        if (fileCache.TryGetValue(fileName, out byte[] cachedFile))
                        {
                            byte[] decryptedFile = DecryptFile(cachedFile);
                            await SendResponseAsync(writer, decryptedFile, "application/octet-stream");
                        }
                        else if (File.Exists(fileName))
                        {
                            byte[] fileData = File.ReadAllBytes(fileName);
                            byte[] decryptedData = DecryptFile(fileData);
                            fileCache[fileName] = decryptedData;
                            await SendResponseAsync(writer, decryptedData, "application/octet-stream");
                        }
                        else
                        {
                            await SendErrorResponseAsync(writer, "File not found");
                        }
                    }
                    else
                    {
                        if (fileCache.TryGetValue(fileName, out byte[] cachedFile))
                        {
                            byte[] encryptedFile = EncryptFile(cachedFile);
                            await SendResponseAsync(writer, encryptedFile, "application/octet-stream");
                        }
                        else if (File.Exists(fileName))
                        {
                            byte[] fileData = File.ReadAllBytes(fileName);
                            byte[] encryptedFile = EncryptFile(fileData);
                            fileCache[fileName] = encryptedFile;
                            await SendResponseAsync(writer, encryptedFile, "application/octet-stream");
                        }
                        else
                        {
                            await SendErrorResponseAsync(writer, "File not found");
                        }
                    }
                }
                else
                {
                    await SendErrorResponseAsync(writer, "Method Not Allowed");
                }
            }
        }
        catch (Exception ec)
        {
            Console.WriteLine($"Error: {ec.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private static async Task SendResponseAsync(BinaryWriter writer, byte[] fileData, string contentType)
    {
        writer.Write(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n"));
        writer.Write(Encoding.ASCII.GetBytes($"Content-Type: {contentType}\r\n"));
        writer.Write(Encoding.ASCII.GetBytes($"Content-Length: {fileData.Length}\r\n"));
        writer.Write(Encoding.ASCII.GetBytes("\r\n"));
        await writer.BaseStream.WriteAsync(fileData, 0, fileData.Length);
    }

    private static async Task SendErrorResponseAsync(BinaryWriter writer, string message)
    {
        writer.Write(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n"));
        writer.Write(Encoding.ASCII.GetBytes("Content-Type: text/plain\r\n"));
        writer.Write(Encoding.ASCII.GetBytes($"Content-Length: {message.Length}\r\n"));
        writer.Write(Encoding.ASCII.GetBytes("\r\n"));
        await writer.BaseStream.WriteAsync(Encoding.ASCII.GetBytes(message), 0, message.Length);
    }

    private static byte[] EncryptFile(byte[] fileData)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = Encoding.UTF8.GetBytes(secretKey);
            aes.IV = Encoding.UTF8.GetBytes(iv);

            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(fileData, 0, fileData.Length);
                    cs.Close();
                }
                return ms.ToArray();
            }
        }
    }

    private static byte[] DecryptFile(byte[] encryptedData)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = Encoding.UTF8.GetBytes(secretKey);
            aes.IV = Encoding.UTF8.GetBytes(iv);

            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream(encryptedData))
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            using (var result = new MemoryStream())
            {
                cs.CopyTo(result);
                return result.ToArray();
            }
        }
    }
}