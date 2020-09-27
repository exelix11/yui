using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;


namespace yui
{
	static class CertLoader
	{
		// https://github.com/dotnet/runtime/issues/19581#issuecomment-581147166
		public static X509Certificate2 FromFile(string filename)
		{
			PemParser pem = PemParser.FromFile(filename);

			using RSA rsa = RSA.Create();
			rsa.ImportPkcs8PrivateKey(pem.PrivateKey, out _);

			using var Cert = new X509Certificate2(pem.Certificate);
			return new X509Certificate2(Cert.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx));
		}
	}

	class PemParser
	{
		public byte[] PrivateKey => GetSection("PRIVATE KEY");
		public byte[] Certificate => GetSection("CERTIFICATE");

		private readonly string PemFile;

		public PemParser(string file)
		{
			PemFile = file;
		}

		public static PemParser FromFile(string path) =>
			new PemParser(System.IO.File.ReadAllText(path));

		private int SectEndOff(string s)
		{
			var off = PemFile.IndexOf($"-----END {s}-----");
			if (off < 0)
				throw new PemSectionEndingNotFoundException(s);
			return off;
		}

		private int SectStartOff(string s)
		{
			var header = $"-----BEGIN {s}-----";
			var off = PemFile.IndexOf(header);

			if (off < 0)
				throw new PemSectionNotFoundException(s);

			return off + header.Length;
		}

		public bool HasSection(string section) =>
			SectStartOff(section) != -1 && SectEndOff(section) != -1;

		public byte[] GetSection(string section)
		{
			var begin = SectStartOff(section);
			var end = SectEndOff(section) - begin;

			return Convert.FromBase64String(PemFile.Substring(begin, end));
		}

		class PemSectionNotFoundException : Exception
		{
			public PemSectionNotFoundException(string message) : base(message) { }
		}

		class PemSectionEndingNotFoundException : Exception
		{
			public PemSectionEndingNotFoundException(string message) : base(message) { }
		}
	}
}
