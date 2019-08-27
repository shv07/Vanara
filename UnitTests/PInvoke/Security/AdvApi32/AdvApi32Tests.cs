﻿using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Vanara.Extensions;
using Vanara.InteropServices;
using static Vanara.PInvoke.AdvApi32;
using static Vanara.PInvoke.Kernel32;

namespace Vanara.PInvoke.Tests
{
	[TestFixture()]
	public class AdvApi32Tests
	{
		internal const SECURITY_INFORMATION AllSI = SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION | SECURITY_INFORMATION.GROUP_SECURITY_INFORMATION | SECURITY_INFORMATION.DACL_SECURITY_INFORMATION | SECURITY_INFORMATION.SACL_SECURITY_INFORMATION;
		internal const string fn = @"C:\Temp\help.ico";

		internal static object[] GetAuthCasesFromFile(bool validUser, bool validCred) => AuthCasesFromFile.Where(objs => ((object[])objs)[0].Equals(validUser) && ((object[])objs)[1].Equals(validCred)).ToArray();

		internal static object[] AuthCasesFromFile
		{
			get
			{
				const string authfn = @"C:\Temp\AuthTestCases.txt";
				var lines = File.ReadAllLines(authfn).Skip(1).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
				var ret = new object[lines.Length];
				for (var i = 0; i < lines.Length; i++)
				{
					var items = lines[i].Split('\t').Select(s => s == string.Empty ? null : s).Cast<object>().ToArray();
					if (items.Length < 9) continue;
					bool.TryParse(items[0].ToString(), out var validUser);
					items[0] = validUser;
					bool.TryParse(items[1].ToString(), out var validCred);
					items[1] = validCred;
					ret[i] = items;
				}
				return ret;
			}
		}

		[Test()]
		public void ConvertSecurityDescriptorToStringSecurityDescriptorTest()
		{
			var pSD = GetSD(fn);
			var b = ConvertSecurityDescriptorToStringSecurityDescriptor(pSD, SDDL_REVISION.SDDL_REVISION_1,
				SECURITY_INFORMATION.DACL_SECURITY_INFORMATION, out var s, out var len);
			Assert.That(b, Is.True);
			Assert.That(s, Is.Not.Null);
			TestContext.WriteLine(s);
		}

		[Test]
		public void CreateProcessWithTokenWTest()
		{
			using (var pTok = SafeHTOKEN.FromProcess(GetCurrentProcess()))
			using (var hTok = pTok.DuplicateImpersonate())
			{
				var b = CreateProcessWithTokenW(hTok, 0, "notepad.exe", null, 0, default, default, STARTUPINFO.Default, out var pi);
				if (!b) TestContext.WriteLine($"CreateProcessWithTokenW:{Win32Error.GetLastError()}");
				Assert.That(b, Is.True);
				Assert.That((int)WaitForSingleObject(pi.hProcess, INFINITE), Is.Zero);
			}
		}

		[Test()]
		public void DuplicateTokenExTest()
		{
			using (var tok = SafeHTOKEN.FromThread(SafeHTHREAD.Current))
			{
				Assert.That(tok.IsInvalid, Is.False);
			}

			using (var tok = SafeHTOKEN.FromThread(GetCurrentThread()))
			{
				Assert.That(tok.IsInvalid, Is.False);
			}
		}

		[Test()]
		public void GetNamedSecurityInfoTest()
		{
			using (var pSD = GetSD(fn))
				Assert.That(pSD, Is.Not.Null);
		}

		[Test()]
		[PrincipalPermission(SecurityAction.Assert, Role = "Administrators")]
		public void SetNamedSecurityInfoTest()
		{
			using (var pSD = GetSD(fn))
			{
				Assert.That(GetSecurityDescriptorOwner(pSD, out var owner, out var def));
				Assert.That((IntPtr)owner, Is.Not.EqualTo(IntPtr.Zero));

				var admins = new SafePSID("S-1-5-32-544");

				var err = SetNamedSecurityInfo(fn, SE_OBJECT_TYPE.SE_FILE_OBJECT, SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION, admins, PSID.NULL, IntPtr.Zero, IntPtr.Zero);
				if (err.Failed) TestContext.WriteLine($"SetNamedSecurityInfo failed: {err}");
				Assert.That(err.Succeeded);
				err = SetNamedSecurityInfo(fn, SE_OBJECT_TYPE.SE_FILE_OBJECT, SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION, owner, PSID.NULL, IntPtr.Zero, IntPtr.Zero);
				if (err.Failed) TestContext.WriteLine($"SetNamedSecurityInfo failed: {err}");
				Assert.That(err.Succeeded);
			}
		}

		[Test]
		public void UserTest()
		{
			GetNamedSecurityInfo(fn, SE_OBJECT_TYPE.SE_FILE_OBJECT, SECURITY_INFORMATION.DACL_SECURITY_INFORMATION, out _, out _, out var ppDacl, out _, out var ppSecurityDescriptor).ThrowIfFailed();

			var aceCount = ppDacl.GetAclInformation<ACL_SIZE_INFORMATION>().AceCount;
			for (var i = 0U; i < aceCount; i++)
			{
				if (!GetAce(ppDacl, i, out var ace)) Win32Error.ThrowLastError();
				var accountSize = 1024;
				var domainSize = 1024;
				var account = new StringBuilder(accountSize, accountSize);
				var domain = new StringBuilder(domainSize, domainSize);
				if (!LookupAccountSid(null, ace.GetSid(), account, ref accountSize, domain, ref domainSize, out _)) Win32Error.ThrowLastError();
				TestContext.WriteLine($"Ace{i}: {ace.GetHeader().AceType}={domain}\\{account}; {ace.GetMask()}");
			}
		}

		internal static SafePSECURITY_DESCRIPTOR GetSD(string filename, SECURITY_INFORMATION si = SECURITY_INFORMATION.DACL_SECURITY_INFORMATION | SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION)
		{
			return GetFileSecurity(filename, si);
		}
	}
}