﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Configuration.Install;
using System.Reflection;
using System.Threading;
using System.Configuration;
using System.Runtime.InteropServices;


using Utilities;
using OpcLibrary;

namespace ShdrService4Opc
{
    public enum RpcAuthnLevel
    {
        Default = 0,
        None = 1,
        Connect = 2,
        Call = 3,
        Pkt = 4,
        PktIntegrity = 5,
        PktPrivacy = 6
    }

    public enum RpcImpLevel
    {
        Default = 0,
        Anonymous = 1,
        Identify = 2,
        Impersonate = 3,
        Delegate = 4
    }

    public enum EoAuthnCap
    {
        None = 0x00,
        MutualAuth = 0x01,
        StaticCloaking = 0x20,
        DynamicCloaking = 0x40,
        AnyAuthority = 0x80,
        MakeFullSIC = 0x100,
        Default = 0x800,
        SecureRefs = 0x02,
        AccessControl = 0x04,
        AppID = 0x08,
        Dynamic = 0x10,
        RequireFullSIC = 0x200,
        AutoImpersonate = 0x400,
        NoCustomMarshal = 0x2000,
        DisableAAA = 0x1000
    }
    
    
    public class Interop
    {
 

    // Create the call with PreserveSig:=FALSE so the COM InterOp
    // layer will perform the error checking and throw an 
    // exception instead of returning an HRESULT.
    //
    [DllImport("Ole32.dll",
           ExactSpelling = true,
           EntryPoint = "CoInitializeSecurity",
           CallingConvention = CallingConvention.StdCall,
           SetLastError = false,
           PreserveSig = false)]

    public static extern long CoInitializeSecurity(
        IntPtr pVoid,
        int cAuthSvc,
        IntPtr asAuthSvc,
        IntPtr pReserved1,
        uint dwAuthnLevel,
        uint dwImpLevel,
        IntPtr pAuthList,
        uint dwCapabilities,
        IntPtr pReserved3);

    /// <summary>
    /// Call CoInitializeSecurity with dwImpLevel set to 
    /// Identity.
    /// </summary>
    public Interop()
    {
        long hres;
        try
        {
            hres = CoInitializeSecurity(IntPtr.Zero,
                -1,
                IntPtr.Zero,
                IntPtr.Zero,
                (uint)RpcAuthnLevel.None,
                (uint)RpcImpLevel.Identify,
                IntPtr.Zero,
                (uint)EoAuthnCap.None,
                IntPtr.Zero);
            if (hres < 0)
            {
                Logger.LogMessage("DCOM CoInitializeSecurity Failed\n", Logger.FATAL);
            }
        }
        catch (Exception e)
        {
            Logger.LogMessage("DCOM CoInitializeSecurity Exception" + e.Message + "\n", Logger.FATAL);

        }
    }
}

    public class MyProgram 
    {

        public Thread runThread = null;
        public bool running = false;
        private bool bTerminating;
        System.Timers.Timer aTimer;

        public TimeSpan dtResetTime;
        public int nResetCycleWait = 2000;
        public bool bReset = true;

        // MTConnect declarations
         int nMTCPort = 0;                       // mt connect agent socket port number
        string[] devices;                       // mtc devices names per opc server
 
        // OPC declarations
        // Configuration parameters
        List<OPCMgr> opcClients = new List<OPCMgr>();
        string[] ipaddrs;
        public bool opcflag = true;
        int nShdrScanDelay;

        int nDebug = 0;                       // display debug information
        string[] sports;
        List<int> ports = new List<int>();


         public void LogMessage(string errmsg, int level)
        {
            if (!errmsg.EndsWith("\n"))
                errmsg += "\n";
            Logger.LogMessage(errmsg, level);
        }

        public MyProgram()
        {
 
        }
        public void MyProgramSetup()
        {
            bTerminating = false;
            // Configuration
            try
            {
                nDebug = Convert.ToInt32(ConfigurationManager.AppSettings["Debug"]);
                Logger.debuglevel = nDebug;
                nMTCPort = Convert.ToInt32(ConfigurationManager.AppSettings["MTConnectPort"]);
                opcflag = Convert.ToBoolean(ConfigurationManager.AppSettings["opcflag"]);
                nResetCycleWait = Convert.ToInt32(ConfigurationManager.AppSettings["ResetCycleWait"]);
                nShdrScanDelay = Convert.ToInt32(ConfigurationManager.AppSettings["ShdrScanDelay"]);
                dtResetTime = TimeSpan.Parse(ConfigurationManager.AppSettings["ResetTime"]);
                bReset = Convert.ToBoolean(ConfigurationManager.AppSettings["ResetFlag"]);
 
                sports = ConfigurationManager.AppSettings["shdrports"].Split(',');// other.AppSettings.Settings["devices"].Value.Split(',');
                for (int j = 0; j < sports.Count(); j++) 
                    ports.Add(Convert.ToInt16(sports[j].Trim()));
 
                ipaddrs = ConfigurationManager.AppSettings["opcipaddrs"].Split(',');// other.AppSettings.Settings["devices"].Value.Split(',');
                for (int i = 0; i < ipaddrs.Count(); i++) ipaddrs[i] = ipaddrs[i].Trim();

                devices = ConfigurationManager.AppSettings["devices"].Split(',');// other.AppSettings.Settings["devices"].Value.Split(',');
                for (int i = 0; i < devices.Count(); i++) devices[i] = devices[i].Trim();
 
                /* Create timer */
                aTimer = new System.Timers.Timer();
                aTimer.Elapsed += new System.Timers.ElapsedEventHandler(OnTimedEvent);
                aTimer.Interval = 1000;
                aTimer.AutoReset = false;

                if (opcflag)
                {
                    for (int i = 0; i < ipaddrs.Count(); i++)
                    {
                        Agent agent = new Agent(ports[i], nShdrScanDelay);
                        OPCMgr opc = new OPCMgr(agent, devices[i], ipaddrs[i]);
                        opc.Init();
                        opcClients.Add(opc);
                        agent.StoreEvent(DateTime.Now.ToString("s"), devices[i], "power", "OFF", null, null, null, null, null, null);
                        agent.Start();
                    }
                    if (opcClients.Count < 1)
                        throw new Exception("Illegal Host Name");
                    aTimer.Interval = opcClients[0].nServerUpdatePeriod;
                }
            }
            catch (Exception e)
            {
                dtResetTime = new TimeSpan(0, 0, 0, 60, 0);
                Logger.LogMessage("Configuration Error: " + e.Message, Logger.FATAL);
            }
        }

        public void Start()
        {

            runThread = new Thread(new ThreadStart(MainLoop));
            runThread.IsBackground = true;
            running = true;

            try
            {
                runThread.Start();
            }
            catch (Exception ex)
            {
                Logger.LogMessage("Exception: MyProgram Start() " + ex.Message + ex.StackTrace, Logger.FATAL);
            }
        }

        public void Stop()
        {
            running = false;
            runThread.Join();
        }

        public void Abort()
        {
            Logger.LogMessage("Abort MTConnect Service" + DateTime.Now.ToString(), -1);
            aTimer.Stop();
            bTerminating = true;
            Thread.Sleep(1000);
            Disconnect();
            Environment.Exit(-1);
        }
        /// <summary>
        /// Thread to determine when to "reset" the service so that OPC 
        /// can function properly.
        /// </summary>
        public void MainLoop()
        {
            // This is here so that the service can start up quickly and we can better determine what ails us
            MyProgramSetup();

            aTimer.Start();
            DateTime date1 = DateTime.Now;
            DateTime date2 = new DateTime(date1.Year, date1.Month, date1.Day, 0, 0, 0);
            //date2 += new TimeSpan(1, 0, 0, 0);
            date2 = date1 + this.dtResetTime;

            while (running)
            {
                if (bReset)
                {
                    date1 = DateTime.Now;
                    if (date1 > date2)
                        Abort();
                }
                Thread.Sleep(nResetCycleWait);
            }
            aTimer.Stop();
            bTerminating = true;
            Thread.Sleep(1000);
            Disconnect();
            Logger.LogMessage("Stop MTConnect Service" + DateTime.Now.ToString(), Logger.FATAL);
        }

        public void Disconnect()
        {
            if (opcflag)
            {
                for (int i = 0; i < opcClients.Count(); i++)
                {
                    opcClients[i].agent.Stop();
                    opcClients[i].Disconnect();
                }
                aTimer.Interval = opcClients[0].nServerRetryPeriod;
            }

        }
        //public void Connect()
        //{
        //    if (opcflag)
        //    {
        //        for (int i = 0; i < opcClients.Count(); i++)
        //        {
        //            opcClients[i].agent.Stop();
        //            opcClients[i].Disconnect();
        //        }
        //        aTimer.Interval = opcClients[0].nServerUpdatePeriod;
        //    }
        //}

        private void OnTimedEvent(object source, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (opcflag)
                {
                    for (int i = 0; i < opcClients.Count(); i++)
                    {
                        if (opcClients[i] == null)
                            continue;

                        opcClients[i].Cycle();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(ex.ToString(), 2);

            }
            if (!bTerminating)
                aTimer.Start();

        }
    }
}

