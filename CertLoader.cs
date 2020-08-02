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
			byte[] key = pem.GetSection("PRIVATE KEY");
			byte[] cert = pem.GetSection("CERTIFICATE");

			using RSA rsa = RSA.Create();
			rsa.ImportPkcs8PrivateKey(key, out _);

			using var Cert = new X509Certificate2(cert);
			return new X509Certificate2(Cert.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx));
		}
	}

	class PemParser
	{
		class PemSectionNotFoundException : Exception
		{
			public PemSectionNotFoundException(string message) : base(message) { }
		}

		private string PemFile;

		public PemParser(string file)
		{
			PemFile = file;
		}

		public static PemParser FromFile(string path) =>
			new PemParser(System.IO.File.ReadAllText(path));

		private int SectEndOff(string s) => PemFile.IndexOf($"-----END {s}-----");

		private int SectStartOff(string s)
		{
			var header = $"-----BEGIN {s}-----";
			var off = PemFile.IndexOf(header);
			return off < 0 ? off : off + header.Length;
		}

		public bool HasSection(string section) =>
			SectStartOff(section) != -1 && SectEndOff(section) != -1;

		public byte[] GetSection(string section)
		{
			if (!HasSection(section))
				throw new PemSectionNotFoundException(section);

			var begin = SectStartOff(section);
			var end = SectEndOff(section) - begin;

			return Convert.FromBase64String(PemFile.Substring(begin, end));
		}

	}
}
