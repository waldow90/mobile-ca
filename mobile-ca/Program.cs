﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace mobile_ca
{
    class Program
    {
        private enum Mode
        {
            INVALID,
            rsa,
            ca,
            cert,
            help,
            server
        }

        private enum Action
        {
            INVALID,
            create,
            install,
            uninstall,
            query
        }

        private struct CmdArgs
        {
            public bool Valid;
            public Mode Mode;
            public Action Action;
            public bool Sha256;
            public bool LM;
            public bool IsFile;
            public bool OpenBrowser;
            public string Thumbprint;
            public int Port;
            public int RsaSize;
            public int Expiration;
            public string Output;
            public string Key;
            public string CAK;
            public string CAC;
            public string CC, ST, L, O, OU, CN, E;
            public List<string> Domains, IPs;

            public void SetDefaults()
            {
                if (Mode == Mode.cert || Mode == Mode.ca)
                {
                    Expiration = Expiration == 0 ? 3650 : Expiration;
                    CC = S(CC, "XX");
                    ST = S(ST, "Local");
                    L = S(L, "Local");
                    O = S(O, "ACME");
                    OU = S(OU, "ACME");
                    CN = S(CN, Mode == Mode.cert ? "localhost" : "ACME Root CA");
                    E = S(E, "ACME@example.com");
                }
            }

            private static string S(string A, string B)
            {
                return string.IsNullOrEmpty(A) ? B : A;
            }
        }

        private const int SUCCESS = 0x00;
        private const int GENERIC_ERROR = 0xFF;

        static int Main(string[] args)
        {
            DateTime Start = DateTime.UtcNow;
            Logger.Info("Application Start at {0}", Start);

            if (CertCommands.ValidateOpenSSL())
            {
                var A = ParseArgs(args);

                //Run Webserver
                //var A = ParseArgs("/http 55555 /b".Split(' '));
                //Generate RSA
                //var A = ParseArgs(@"/rsa 2048 /out Data\Cert.key".Split(' '));
                //Generate CA
                //var A = ParseArgs(@"/ca /key C:\temp\rsa.txt /out C:\temp\CA.crt".Split(' '));
                //Install CA
                //var A = ParseArgs(@"/ca /install C:\temp\CA.crt".Split(' '));
                //Check if CA installed
                //var A = ParseArgs(@"/ca /query C:\temp\CA.crt /F".Split(' '));
                //Uninstall CA
                //var A = ParseArgs(@"/ca /uninstall C:\temp\CA.crt /F".Split(' '));
                //Create Certificate with CA
                //var A = ParseArgs(@"/cert /key Data\01b72657-c0fb-4738-ae1d-b9a1736f14e9.key /CAC Data\DF74671747C7CBC421005CFD87E915E5751ABBDC.ca.crt /CAK Data\8a7f4b5a-fe00-4212-ac7e-9fb1aa1f3347.key /CN test.com /DN *.test.com /IP 1.1.1.1 /IP ::1 /out Data\Cert.crt".Split(' '));
#if DEBUG
                Props(A);
#endif
                if (A.Mode == Mode.help)
                {
#if DEBUG
                    Logger.Debug("Would show Help here");
#else
                Help();
#endif
                    return SUCCESS;
                }
                if (A.Valid)
                {
                    if (A.Mode == Mode.server)
                    {
                        using (Server S = new Server(A.Port, A.OpenBrowser))
                        {
                            do
                            {
                                Logger.Info("Press [ESC] to exit");
                            } while (WaitForKey() != ConsoleKey.Escape);
                        }
                    }
                    else if (A.Mode == Mode.rsa)
                    {
                        var Key = CertCommands.GenerateKey(A.RsaSize);
                        if (A.Output != null)
                        {
                            try
                            {
                                File.WriteAllText(A.Output, Key);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Unable to write key to {0}. Reason: {1}", A.Output, ex.Message);
                                //Log the key to console so it's not lost
                                Console.WriteLine(Key);
                                return GENERIC_ERROR;
                            }
                        }
                        else
                        {
                            Console.WriteLine(Key);
                        }
                    }
                    else if (A.Mode == Mode.ca)
                    {
                        if (A.IsFile && (A.Action == Action.query || A.Action == Action.uninstall))
                        {
                            A.Thumbprint = ReadAll(A.Thumbprint);
                            if (A.Thumbprint != null)
                            {
                                try
                                {
                                    A.Thumbprint = CertStore.GetThumb(A.Thumbprint);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error("Unable to read certificate {0}. Reason: {1}", A.Thumbprint, ex.Message);
                                    return GENERIC_ERROR;
                                }
                            }
                            else
                            {
                                return GENERIC_ERROR;
                            }
                        }
                        switch (A.Action)
                        {
                            case Action.create:
                                A.Key = ReadAll(A.Key);
                                if (A.Key == null)
                                {
                                    return GENERIC_ERROR;
                                }
                                string CACert = null;
                                try
                                {
                                    CACert = CertCommands.GenerateRootCert(A.Key, A.Expiration, A.Sha256, A.CC, A.ST, A.L, A.O, A.OU, A.CN, A.E);
                                    if (string.IsNullOrEmpty(CACert))
                                    {
                                        throw new Exception("Openssl did not return a result");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error("Unable to create CA certificate. Reason: {0}", ex.Message);
                                    return GENERIC_ERROR;
                                }
                                if (A.Output != null)
                                {
                                    try
                                    {
                                        File.WriteAllText(A.Output, CACert);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error("Unable to write cert to {0}. Reason: {1}", A.Output, ex.Message);
                                        //Log the key to console so it's not lost
                                        Console.WriteLine(CACert);
                                        return GENERIC_ERROR;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine(CACert);
                                }
                                break;
                            case Action.install:
                                A.CAC = ReadAll(A.CAC);
                                if (A.CAC != null)
                                {
                                    CertStore.InstallRoot(A.CAC, A.LM);
                                }
                                else
                                {
                                    Logger.Error("Unable to read Certificate file");
                                }
                                break;
                            case Action.query:
                                if (CertStore.HasCert(A.Thumbprint))
                                {
                                    Logger.Info("Certificate {0} is installed", A.Thumbprint);
                                }
                                else
                                {
                                    Logger.Info("Certificate {0} is NOT installed", A.Thumbprint);
                                    return GENERIC_ERROR;
                                }
                                break;
                            case Action.uninstall:
                                if (CertStore.RemoveRoot(A.Thumbprint, A.LM) > 0)
                                {
                                    Logger.Info("Certificate {0} uninstalled", A.Thumbprint);
                                }
                                else
                                {
                                    if (!CertStore.HasCert(A.Thumbprint))
                                    {
                                        Logger.Warn("Certificate {0} not found in store", A.Thumbprint);
                                    }
                                    else
                                    {
                                        Logger.Info("Certificate {0} not uninstalled", A.Thumbprint);
                                    }
                                    return GENERIC_ERROR;
                                }
                                break;
                        }
                    }
                    else if (A.Mode == Mode.cert)
                    {
                        switch (A.Action)
                        {
                            case Action.create:
                                A.Key = ReadAll(A.Key);
                                A.CAC = ReadAll(A.CAC);
                                A.CAK = ReadAll(A.CAK);
                                if (A.Key == null || A.CAC == null || A.CAK == null)
                                {
                                    return GENERIC_ERROR;
                                }
                                string Cert = null;
                                try
                                {
                                    Cert = CertCommands.GenerateCertificate(A.CAK, A.CAC, A.Key, A.CN, A.IPs.Concat(A.Domains).ToArray(), A.Expiration, A.Sha256, A.CC, A.ST, A.L, A.O, A.OU, A.E);
                                    if (string.IsNullOrEmpty(Cert))
                                    {
                                        throw new Exception("Openssl did not return a result");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error("Unable to create certificate. Reason: {0}", ex.Message);
                                    return GENERIC_ERROR;
                                }
                                if (A.Output != null)
                                {
                                    try
                                    {
                                        File.WriteAllText(A.Output, Cert);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error("Unable to write cert to {0}. Reason: {1}", A.Output, ex.Message);
                                        //Log the key to console so it's not lost
                                        Console.WriteLine(Cert);
                                        return GENERIC_ERROR;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine(Cert);
                                }
                                break;
                        }
                    }
                }
                else
                {
                    Logger.Error("Invalid Arguments");
                }
            }
            else
            {
                Logger.Error("openssl can't be found. Files needed:\r\nopenssl.exe\r\nssleay32.dll\r\nlibeay32.dll");
            }
            Logger.Log("Application Runtime: {0}ms", (ulong)DateTime.UtcNow.Subtract(Start).TotalMilliseconds);
#if DEBUG
            Logger.Debug("#END - Press any key to exit");
            WaitForKey();
#endif
            return SUCCESS;
        }

        private static CmdArgs ParseArgs(string[] args)
        {
            var A = new CmdArgs();
            A.Mode = Mode.INVALID;
            A.Action = Action.INVALID;
            A.Domains = new List<string>();
            A.IPs = new List<string>();
            if (args == null || args.Length == 0 || HelpRequest(args))
            {
                A.Mode = Mode.help;
                A.Valid = true;
                return A;
            }
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var hasMore = args.Length > i + 1;

                if (A.Mode == Mode.INVALID)
                {
                    #region Mode
                    switch (arg.ToLower())
                    {
                        case "/http":
                            A.Mode = Mode.server;
                            break;
                        case "/rsa":
                            A.Mode = Mode.rsa;
                            break;
                        case "/ca":
                            A.Mode = Mode.ca;
                            break;
                        case "/cert":
                            A.Mode = Mode.cert;
                            break;
                        default:
                            Logger.Error("Invalid mode: {0}", arg);
                            return A;
                    }
                    #endregion
                }
                else if (A.Action == Action.INVALID)
                {
                    #region Action
                    switch (arg.ToLower())
                    {
                        case "/install":
                            if (A.Mode == Mode.ca)
                            {
                                A.Action = Action.install;
                            }
                            else
                            {
                                Logger.Error("Invalid mode for /ca: {0}", arg);
                                return A;
                            }
                            break;
                        case "/query":
                            if (A.Mode == Mode.ca)
                            {
                                A.Action = Action.query;
                            }
                            else
                            {
                                Logger.Error("Invalid mode for /ca: {0}", arg);
                                return A;
                            }
                            break;
                        case "/uninstall":
                            if (A.Mode == Mode.ca)
                            {
                                A.Action = Action.uninstall;
                            }
                            else
                            {
                                Logger.Error("Invalid mode for /ca: {0}", arg);
                                return A;
                            }
                            break;
                        case "/key":
                            if (A.Mode != Mode.rsa)
                            {
                                if (hasMore)
                                {
                                    A.Action = Action.create;
                                    A.Key = args[++i];
                                }
                                else
                                {
                                    Logger.Error("{0} requires a file name", arg);
                                    return A;
                                }

                            }
                            else
                            {
                                Logger.Error("Invalid mode for /rsa: {0}", arg);
                                return A;
                            }
                            break;
                        default:
                            if (A.Mode == Mode.server)
                            {
                                if (Server.IsValidPort(IntOrDefault(arg, -1)))
                                {
                                    A.Port = IntOrDefault(arg);
                                    A.Action = Action.create;
                                }
                                else
                                {
                                    Logger.Error("Invalid port number: {0}", arg);
                                    return A;
                                }
                            }
                            else if (A.Mode == Mode.rsa)
                            {
                                if (CertCommands.IsValidKeySize(IntOrDefault(arg)))
                                {
                                    A.RsaSize = IntOrDefault(arg);
                                    A.Action = Action.create;
                                }
                                else
                                {
                                    Logger.Error("Invalid RSA key size: {0}", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("Invalid action: {0}", arg);
                                return A;
                            }
                            break;
                    }
                    #endregion
                }
                else
                {
                    #region Params
                    switch (arg.ToLower())
                    {
                        case "/b":
                            if (A.Mode == Mode.server)
                            {
                                if (!A.OpenBrowser)
                                {
                                    A.OpenBrowser = true;
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            break;
                        case "/lm":
                            if (A.Mode == Mode.ca && (A.Action == Action.install || A.Action == Action.query || A.Action == Action.uninstall))
                            {
                                if (!A.LM)
                                {
                                    A.LM = true;
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            break;
                        case "/exp":
                            if ((A.Mode == Mode.cert || A.Mode == Mode.ca) && A.Action == Action.create)
                            {
                                if (A.Expiration == 0)
                                {
                                    A.Expiration = IntOrDefault(args[++i]);
                                    if (A.Expiration < 1)
                                    {
                                        Logger.Error("Invalid expiration: {0}", args[i]);
                                        return A;
                                    }
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            break;
                        case "/256":
                            if ((A.Mode == Mode.cert || A.Mode == Mode.ca) && A.Action == Action.create)
                            {
                                if (!A.Sha256)
                                {
                                    A.Sha256 = true;
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            break;
                        case "/key":
                            if (hasMore)
                            {
                                if (A.Key == null)
                                {
                                    A.Key = args[++i];
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} requires a file name", arg);
                                return A;
                            }
                            break;
                        case "/dn":
                        case "/ip":
                            if (hasMore)
                            {
                                if (A.Mode == Mode.cert && A.Action == Action.create)
                                {
                                    if (arg.ToLower() == "/ip")
                                    {
                                        if (CertCommands.IsValidIp(args[i + 1]))
                                        {
                                            A.IPs.Add(args[++i]);
                                        }
                                        else
                                        {
                                            Logger.Error("{0} requires a valid IP, {1} given", arg, args[i + 1]);
                                            return A;
                                        }
                                    }
                                    else
                                    {
                                        if (CertCommands.IsValidDomainName(args[i + 1]))
                                        {
                                            A.Domains.Add(args[++i]);
                                        }
                                        else
                                        {
                                            Logger.Error("{0} requires a valid Domain, {1} given", arg, args[i + 1]);
                                            return A;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Logger.Error("{0} requires a value", arg);
                                return A;
                            }
                            break;
                        case "/f":
                            if (A.Mode == Mode.ca && (A.Action == Action.query || A.Action == Action.uninstall))
                            {
                                if (!A.IsFile)
                                {
                                    A.IsFile = true;
                                }
                                else
                                {
                                    Logger.Error("{0} specified multiple times", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            break;
                        case "/ou":
                        case "/o":
                        case "/cc":
                        case "/st":
                        case "/l":
                        case "/cn":
                        case "/e":
                        case "/cac":
                        case "/cak":
                            #region CertProps
                            if (A.Mode == Mode.cert || (A.Mode == Mode.ca && A.Action == Action.create))
                            {
                                if (hasMore)
                                {
                                    switch (arg.ToLower())
                                    {
                                        case "/cac":
                                            if (A.Mode == Mode.cert)
                                            {
                                                if (hasMore)
                                                {
                                                    if (A.CAC == null)
                                                    {
                                                        A.CAC = args[++i];
                                                    }
                                                    else
                                                    {
                                                        Logger.Error("{0} defined multiple times", arg);
                                                        return A;
                                                    }
                                                }
                                                else
                                                {
                                                    Logger.Error("{0} requires a file name", arg);
                                                    return A;
                                                }
                                            }
                                            else
                                            {
                                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                                return A;
                                            }
                                            break;
                                        case "/cak":
                                            if (A.Mode == Mode.cert)
                                            {
                                                if (hasMore)
                                                {
                                                    if (A.CAK == null)
                                                    {
                                                        A.CAK = args[++i];
                                                    }
                                                    else
                                                    {
                                                        Logger.Error("{0} defined multiple times", arg);
                                                        return A;
                                                    }
                                                }
                                                else
                                                {
                                                    Logger.Error("{0} requires a file name", arg);
                                                    return A;
                                                }
                                            }
                                            else
                                            {
                                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                                return A;
                                            }
                                            break;
                                        case "/ou":
                                            if (A.OU == null)
                                            {
                                                A.OU = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }
                                            break;
                                        case "/o":
                                            if (A.O == null)
                                            {
                                                A.O = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }
                                            break;
                                        case "/cc":
                                            if (A.CC == null)
                                            {
                                                A.CC = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }
                                            break;
                                        case "/st":
                                            if (A.ST == null)
                                            {
                                                A.ST = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }
                                            break;
                                        case "/l":
                                            if (A.L == null)
                                            {
                                                A.L = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }

                                            break;
                                        case "/cn":
                                            if (A.CN == null)
                                            {
                                                A.CN = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }

                                            break;
                                        case "/e":
                                            if (A.E == null)
                                            {
                                                A.E = args[++i];
                                            }
                                            else
                                            {
                                                Logger.Error("{0} defined multiple times", arg);
                                                return A;
                                            }

                                            break;
                                    }
                                }
                                else
                                {
                                    Logger.Error("{0} requires a value", arg);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("{0} is not supported for mode {1} and action {2}", arg, A.Mode, A.Action);
                                return A;
                            }
                            #endregion
                            break;
                        case "/out":
                            if (args.Length > i + 1)
                            {
                                if (A.Mode == Mode.rsa || A.Mode == Mode.cert || (A.Mode == Mode.ca && A.Action == Action.create))
                                {
                                    A.Output = args[++i];
                                }
                                else
                                {
                                    Logger.Error("/out is not supported for mode {0} and action {1}", A.Mode, A.Action);
                                    return A;
                                }
                            }
                            else
                            {
                                Logger.Error("/out requires a file name");
                                return A;
                            }
                            break;
                        default:
                            //The first argument is to be treated special sometimes
                            if (i == 2 && A.Mode == Mode.ca && (A.Action == Action.install || A.Action == Action.query || A.Action == Action.uninstall))
                            {
                                if (A.Action == Action.install)
                                {
                                    if (A.CAC == null)
                                    {
                                        A.CAC = arg;
                                    }
                                    else
                                    {
                                        Logger.Error("{0} specified multiple times", arg);
                                        return A;
                                    }
                                }
                                else
                                {
                                    if (A.Thumbprint == null)
                                    {
                                        A.Thumbprint = arg;
                                    }
                                    else
                                    {
                                        Logger.Error("{0} specified multiple times", arg);
                                        return A;
                                    }
                                }
                            }
                            else
                            {
                                Logger.Error("Unsupported argument: {0}", arg);
                                return A;
                            }
                            break;
                    }
                    #endregion
                }
            }
            #region Validation
            if (A.Mode == Mode.rsa && !CertCommands.IsValidKeySize(A.RsaSize))
            {
                Logger.Error("/RSA misses key size argument");
                return A;
            }
            if (A.Mode == Mode.ca)
            {
                switch (A.Action)
                {
                    case Action.create:
                        if (A.Key == null)
                        {
                            Logger.Error("RSA Key not specified");
                            return A;
                        }
                        break;
                    case Action.install:
                        if (A.CAC == null)
                        {
                            Logger.Error("Certificate not specified");
                            return A;
                        }
                        break;
                    case Action.query:
                    case Action.uninstall:
                        if (A.Thumbprint == null)
                        {
                            Logger.Error("Thumbprint not specified");
                            return A;
                        }
                        break;
                }
            }
            if (A.Mode == Mode.cert)
            {
                if (A.Key == null)
                {
                    Logger.Error("RSA Key not specified");
                    return A;
                }
                if (A.CAC == null)
                {
                    Logger.Error("Root Certificate not specified");
                    return A;
                }
                if (A.CAK == null)
                {
                    Logger.Error("Root RSA Key not specified");
                    return A;
                }
            }

            A.SetDefaults();

            #endregion
            A.Valid = true;
            return A;
        }

        private static void Help()
        {
            Write(Console.Out, string.Format(@"mobile-ca  |  A portable certificate authority

This tool can be used to simulate a simple certificate authority.

Parameter Format
================

The Parameter help shows which arguments are required and which are optional.
For optional arguments the default value is shown if applicable

RSA Keys
========

Creation of RSA Keys

Create
------

mobile-ca /RSA <size> [/OUT <filename>]

Generates an RSA key of the given size. Supported sizes are {0}.

/OUT  - Optional; Writes certificate to the given file instead of the console

Root Certificates
=================

Handling of root certificate is explained below.
Be aware that using the /LM parameter for installing and uninstalling certificates requires administrative rights.

Create
------

mobile-ca /CA /KEY <keyfile> [/256] [/EXP <days>] [/CC <country-code>] [/ST <state>] [/L <locality-town>] [/O <organization>] [/OU <department>] [/CN <common-name>] [/E <E-Mail>] [/OUT <filename>]

/KEY  - Required; RSA Private key file. Must be first argument
/256  - Optional; Use sha256 instead of sha1 (recommended)
/EXP  - Optional(3650); Number of days after which the cert expires
/CC   - Optional(XX); 2-digit Country code
/ST   - Optional(Local); State
/L    - Optional(Local); Locality, usually the name of the town
/O    - Optional(ACME); Company name
/OU   - Optional(ACME); Name of the department dealing with the Cert
/CN   - Optional(ACME Root CA); Name of the certificate
/E    - Optional(ACME@example.com); E-Mail address for certificate issues.
/OUT  - Optional; Writes certificate to the given file instead of the console


Install
-------

mobile-ca /CA /INSTALL <cert-file> [/LM]

Installs the given certificate into the root store of the user or the local machine.

/INSTALL  - Required; Certificate file to install. Must be first argument
/LM       - Optional(LU); Uses the local machine store instead of the local user store

Due to how windows works, you will be asked to confirm this action.
The thumbprint of the certificate is written to the console output.

The application exit code is set to a non-zero value if the cert was not installed.

Query
-----

mobile-ca /CA /QUERY <thumbprint> [/F] [/LM]

Checks if the given certificate thumbprint is installed in the root certificate store.

/QUERY  - Required; Thumbprint to query for. Must be first argument
/F      - Optional; Interpret Thumbprint argument as certificate file name and extract real thumbprint from it.
/LM     - Optional(LU); Uses the local machine store instead of the local user store

The application exit code is zero if the certificate is found.

Uninstall
---------

mobile-ca /CA /UNINSTALL <thumbprint> [/F] [/LM]

Removes a certificate from the user or computer root store.

/UNINSTALL - Required; Thumbprint of certificate to uninstall. Must be first argument
/F         - Optional; Interpret Thumbprint argument as certificate file name and extract real thumbprint from it.
/LM        - Optional(LU); Uses the local machine store instead of the local user store

The application exit code is set to the number of certificates removed

Certificates
============

Create
------

This is similar to creating a CA certificate but has a few additional parameters

mobile-ca /CERT /KEY <keyfile> /CAC <ca-cert> /CAK <ca-key> [/256] [/EXP <days>] [/CC <country-code>] [/ST <state>] [/L <locality-town>] [/O <organization>] [/OU <department>] [/CN <common-name>] [/E <E-Mail>] [/DN <domain>] [/IP <ip>] [/OUT <filename>]

/KEY  - Required; RSA Private key file. Must be first argument
/CAC  - Required; CA Certificate file
/CAK  - Required; CA RSA Private key file
/256  - Optional; Use sha256 instead of sha1 (recommended)
/EXP  - Optional(3650); Number of days after which the cert expires
/CC   - Optional(XX); 2-digit Country code
/ST   - Optional(Local); State
/L    - Optional(Local); Locality, usually the name of the town
/O    - Optional(ACME); Company name
/OU   - Optional(ACME); Name of the department dealing with the cert
/CN   - Optional(localhost); Primary domain name of the certificate
/DN   - Optional; Additional domain names to add to the certificate, this argument is repeatable
/IP   - Optional; Additional IP addresses to add to the certificate, this argument is repeatable
/E    - Optional(ACME@example.com); E-Mail address for certificate issues.
/OUT  - Optional; Writes certificate to the given file instead of the console

To make a wildcard certificate, prefix a domain with '*.', for example *.example.com.
Be aware that this wildcard cert will be valid for test.example.com but not example.com itself.

", string.Join(", ", CertCommands.ValidKeySizes)), Console.BufferWidth - 1);
        }

        private static bool HelpRequest(IEnumerable<string> args)
        {
            return args.Count() > 0 && args.First() == "/?";
        }

        private static void Write(TextWriter Output, string Text, int LineLength = -1)
        {
            char[] Spaces = Unicode.Get(UnicodeCategory.SpaceSeparator);
            char[] LineBreaks = Unicode.Get(UnicodeCategory.ParagraphSeparator)
                .Concat(Unicode.Get(UnicodeCategory.LineSeparator))
                .Concat(Unicode.Get(UnicodeCategory.Control))
                .ToArray();
            var Lines = Text.Replace("\r\n", "\n").Split(LineBreaks);

            if (LineLength == -1)
            {
                LineLength = Console.BufferWidth;
            }
            if (LineLength < 1)
            {
                throw new ArgumentOutOfRangeException("LineLength");
            }
            foreach (var Line in Lines)
            {
                var LinePos = 0;
                var Words = Line.Split(Spaces);
                foreach (var Word in Words)
                {
                    if (Word.Length > LineLength)
                    {
                        if (LinePos > 0)
                        {
                            Output.WriteLine();
                        }
                        Output.WriteLine(Word.Substring(0, LineLength - 4) + "...");
                        LinePos = 0;
                    }
                    else if (LinePos + Word.Length < LineLength)
                    {
                        Output.Write("{0} ", Word);
                        LinePos += Word.Length + 1;
                    }
                    else
                    {
                        Output.WriteLine();
                        Output.Write("{0} ", Word);
                        LinePos = Word.Length + 1;
                    }
                }
                Output.WriteLine();
            }
        }

#if DEBUG
        /// <summary>
        /// Tests cert system by making a root Cert and perform a proper request
        /// </summary>
        private static void _TEST()
        {
            //You want to use at least 2048 for real certificates (better 4096) but for testing we want it to be fast only.
            var Key1 = CertCommands.GenerateKey(CertCommands.ValidKeySizes.Min());
            var Key2 = CertCommands.GenerateKey(CertCommands.ValidKeySizes.Min());
            Logger.Log("Got Key");
            var CA = CertCommands.GenerateRootCert(Key1);
            Logger.Log("Got CA");
            var CRT = CertCommands.GenerateCertificate(Key1, CA, Key2, "test.local", "a.sub.test.local ::1 *.test.local 123.123.123.123".Split(' '));
            Logger.Log("Got Cert");

            Logger.Log("Installing root certificate");
            var Hash = CertStore.GetThumb(CA);
            if (CertStore.InstallRoot(CA))
            {
                Logger.Log("Installed Cert {0} into CA store", Hash);
            }
            else
            {
                Logger.Error("Root CA not installed.");
            }

            using (var F = new KillHandle(@"C:\Users\Administrator\Desktop\CA_TEST.crt"))
            {
                F.WriteAllText(CA);
                using (var P = System.Diagnostics.Process.Start(F.FileName))
                {
                    P.WaitForExit();
                }
            }
            using (var F = new KillHandle(@"C:\Users\Administrator\Desktop\CRT_TEST.crt"))
            {
                F.WriteAllText(CRT);
                using (var P = System.Diagnostics.Process.Start(F.FileName))
                {
                    P.WaitForExit();
                }
            }

            Logger.Log("Removing root certificate");
            Logger.Log("Removed {0} Certs from CA store", CertStore.RemoveRoot(Hash));
        }

        /// <summary>
        /// Lists all properties and fields from an object
        /// </summary>
        /// <param name="o">Object</param>
        public static void Props(object o)
        {
            if (o == null)
            {
                Console.Error.WriteLine("object is <null>");
                return;
            }
            var T = o.GetType();
            var PLen = T.GetProperties().Select(m => m.Name.Length).ToArray();
            var FLen = T.GetFields().Select(m => m.Name.Length).ToArray();
            var Len = Math.Max(PLen.Length > 0 ? PLen.Max() : 0, FLen.Length > 0 ? FLen.Max() : 0);

            Console.Error.WriteLine("Object: {0}", T.FullName);
            Console.Error.WriteLine("Properties:");
            foreach (var P in T.GetProperties())
            {
                var Value = P.GetValue(o);
                if (Value is IEnumerable && !(Value is string) && !(Value is byte[]))
                {
                    Console.Error.WriteLine("{0}: <enumerable>", P.Name.PadRight(Len));
                    var E = Value as IEnumerable;
                    foreach (var EE in E)
                    {
                        Console.Error.WriteLine("{0}: {1}", string.Empty.PadRight(Len), EE);
                    }
                }
                else
                {
                    if (Value is byte[])
                    {
                        Console.Error.WriteLine("{0}: {1}", P.Name.PadRight(Len), BitConverter.ToString((byte[])Value, 0, ((byte[])Value).Length));
                    }
                    else
                    {
                        Console.Error.WriteLine("{0}: {1}", P.Name.PadRight(Len), Value);
                    }
                }
            }
            foreach (var F in T.GetFields())
            {
                var Value = F.GetValue(o);
                if (Value is IEnumerable)
                {
                    Console.Error.WriteLine("{0}: <enumerable>", F.Name.PadRight(Len));
                    var E = Value as IEnumerable;
                    foreach (var EE in E)
                    {
                        Console.Error.WriteLine("{0}: {1}", string.Empty.PadRight(Len), EE);
                    }
                }
                else
                {
                    Console.Error.WriteLine("{0}: {1}", F.Name.PadRight(Len), F.GetValue(o));
                }
            }
        }
#endif
        /// <summary>
        /// Waits for a key press
        /// </summary>
        /// <returns>Pressed key</returns>
        /// <remarks>Flushes Keyboard buffer before</remarks>
        private static ConsoleKey WaitForKey()
        {
            while (Console.KeyAvailable)
            {
                Console.ReadKey(true);
            }
            return Console.ReadKey(true).Key;
        }

        private static string ReadAll(string FileName)
        {
            try
            {
                return File.ReadAllText(FileName);
            }
            catch (Exception ex)
            {
                Logger.Error("Can't read {0}. Reason: {1}", FileName, ex.Message);
                return null;
            }
        }

        private static int IntOrDefault(object o, int Default = 0)
        {
            int i = 0;
            return o == null ? Default : (int.TryParse(o.ToString(), out i) ? i : Default);
        }
    }
}
