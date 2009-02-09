﻿/*
  Copyright (C) 2005-2008 Govert van Drimmelen

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.


  Govert van Drimmelen
  govert@icon.co.za
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.InteropServices;

namespace ExcelDna.Loader
{
    internal delegate short fn_short_void();
    internal delegate void fn_void_intptr(IntPtr intPtr);
    internal delegate IntPtr fn_intptr_intptr(IntPtr intPtr);

    internal struct XlAddInExportInfo
    {
        #pragma warning disable 0649 // Field 'field' is never assigned to, and will always have its default value 'value'
        internal Int32 ExportInfoVersion;
        internal IntPtr /* PFN_SHORT_VOID */ pXlAutoOpen;
        internal IntPtr /* PFN_SHORT_VOID */ pXlAutoClose;
        internal IntPtr /* PFN_SHORT_VOID */ pXlAutoAdd;
        internal IntPtr /* PFN_SHORT_VOID */ pXlAutoRemove;
        internal IntPtr /* PFN_VOID_LPXLOPER */ pXlAutoFree;
        internal IntPtr /* PFN_VOID_LPXLOPER12 */ pXlAutoFree12;
        internal IntPtr /* PFN_LPXLOPER_LPXLOPER */ pXlAddInManagerInfo;
        internal IntPtr /* PFN_LPXLOPER12_LPXLOPER12 */ pXlAddInManagerInfo12;
        internal Int32 ThunkTableLength;  // Must be EXPORT_COUNT
        internal IntPtr /*PFN*/ ThunkTable; // Actually (PFN ThunkTable[EXPORT_COUNT])
        #pragma warning restore 0649
    };

    public static class XlLibrary
    {
        static int thunkTableLength;
        static IntPtr thunkTable;

        public static unsafe bool Initialize(int xlAddInExportInfoAddress, int hModuleXll, string pathXll)
        {
            Debug.Assert(xlAddInExportInfoAddress != 0);
            Debug.Print("InitializationInfo Address: 0x{0:x8}", xlAddInExportInfoAddress);
            XlAddInExportInfo* pXlAddInExportInfo = (XlAddInExportInfo*)xlAddInExportInfoAddress;
            if (pXlAddInExportInfo->ExportInfoVersion != 1)
            {
                Debug.Print("ExportInfoVersion not supported.");
                return false;
            }

            fn_short_void fnXlAutoOpen = (fn_short_void)XlAutoOpen;
            GCHandle.Alloc(fnXlAutoOpen);
            pXlAddInExportInfo->pXlAutoOpen = Marshal.GetFunctionPointerForDelegate(fnXlAutoOpen);
            fn_short_void fnXlAutoClose = (fn_short_void)XlAutoClose;
            GCHandle.Alloc(fnXlAutoClose);
            pXlAddInExportInfo->pXlAutoClose = Marshal.GetFunctionPointerForDelegate(fnXlAutoClose);
            fn_short_void fnXlAutoAdd = (fn_short_void)XlAutoAdd;
            GCHandle.Alloc(fnXlAutoAdd);
            pXlAddInExportInfo->pXlAutoAdd = Marshal.GetFunctionPointerForDelegate(fnXlAutoAdd);
            fn_short_void fnXlAutoRemove = (fn_short_void)XlAutoRemove;
            GCHandle.Alloc(fnXlAutoRemove);
            pXlAddInExportInfo->pXlAutoRemove = Marshal.GetFunctionPointerForDelegate(fnXlAutoRemove);
            fn_void_intptr fnXlAutoFree = (fn_void_intptr)XlAutoFree;
            GCHandle.Alloc(fnXlAutoFree);
            pXlAddInExportInfo->pXlAutoFree = Marshal.GetFunctionPointerForDelegate(fnXlAutoFree);
            fn_void_intptr fnXlAutoFree12 = (fn_void_intptr)XlAutoFree12;
            GCHandle.Alloc(fnXlAutoFree12);
            pXlAddInExportInfo->pXlAutoFree12 = Marshal.GetFunctionPointerForDelegate(fnXlAutoFree12);
            fn_intptr_intptr fnXlAddInManagerInfo = (fn_intptr_intptr)XlAddInManagerInfo;
            GCHandle.Alloc(fnXlAddInManagerInfo);
            pXlAddInExportInfo->pXlAddInManagerInfo = Marshal.GetFunctionPointerForDelegate(fnXlAddInManagerInfo);
            fn_intptr_intptr fnXlAddInManagerInfo12 = (fn_intptr_intptr)XlAddInManagerInfo12;
            GCHandle.Alloc(fnXlAddInManagerInfo12);
            pXlAddInExportInfo->pXlAddInManagerInfo12 = Marshal.GetFunctionPointerForDelegate(fnXlAddInManagerInfo12);

            thunkTableLength = pXlAddInExportInfo->ThunkTableLength;
            thunkTable = pXlAddInExportInfo->ThunkTable;

            XlLibrary.hModuleXll = (IntPtr)hModuleXll;
            XlLibrary.pathXll = pathXll;

            bool result = false;
            try
            {
                InitializeIntegration();
                result = true;
            }
            catch (Exception e)
            {
                Debug.WriteLine("XlLibrary: Initialize Exception: " + e);
            }

            return result;
        }

        private static unsafe void SetJump(int fi, IntPtr pfn)
        {
            if (fi >= 0 && fi < thunkTableLength)
            {
                void** pThunkTable = (void**)(thunkTable);
                pThunkTable[fi] = (void*)pfn;
            }
        }

        private static void InitializeIntegration()
        {
            AssemblyManager.Initialize(hModuleXll, pathXll);
            Assembly integrationAssembly = Assembly.Load("ExcelDna.Integration");

            Type integrationType = integrationAssembly.GetType("ExcelDna.Integration.Integration");

            MethodInfo tryExcelImplMethod = typeof(XlCallImpl).GetMethod("TryExcelImpl", BindingFlags.Static | BindingFlags.Public);
            Type tryExcelImplDelegateType = integrationAssembly.GetType("ExcelDna.Integration.TryExcelImplDelegate");
            Delegate tryExcelImplDelegate = Delegate.CreateDelegate(tryExcelImplDelegateType, tryExcelImplMethod);
            integrationType.InvokeMember("SetTryExcelImpl", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, null, new object[] { tryExcelImplDelegate });

            MethodInfo registerMethodsMethod = typeof(XlLibrary).GetMethod("RegisterMethods", BindingFlags.Static | BindingFlags.Public);
            Type registerMethodsDelegateType = integrationAssembly.GetType("ExcelDna.Integration.RegisterMethodsDelegate");
            Delegate registerMethodsDelegate = Delegate.CreateDelegate(registerMethodsDelegateType, registerMethodsMethod);
            integrationType.InvokeMember("SetRegisterMethods", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, null, new object[] { registerMethodsDelegate });

            MethodInfo getAssemblyBytesMethod = typeof(AssemblyManager).GetMethod("GetAssemblyBytes", BindingFlags.Static | BindingFlags.NonPublic);
            Type getAssemblyBytesDelegateType = integrationAssembly.GetType("ExcelDna.Integration.GetAssemblyBytesDelegate");
            Delegate getAssemblyBytesDelegate = Delegate.CreateDelegate(getAssemblyBytesDelegateType, getAssemblyBytesMethod);
            integrationType.InvokeMember("SetGetAssemblyBytesDelegate", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, null, new object[] { getAssemblyBytesDelegate });

            // set up helpers (including marshaling helpers)
            IntegrationHelpers.Initialize(integrationAssembly);

            // Now we are ready to call into the loader assembly.
            integrationType.InvokeMember("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, null, null);
        }

        internal static short XlAutoOpen()
        {
            short result = 0;
            try
            {
                result = AutoOpen();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            return result;
        }

        internal static short XlAutoClose()
        {
            Debug.Print("In Managed XlAutoClose");
            return 1;
        }

        internal static short XlAutoAdd()
        {
            Debug.Print("In Managed XlAutoAdd");
            return 1;
        }

        internal static short XlAutoRemove()
        {
            Debug.Print("In Managed XlAutoRemove");
            return 1;
        }

        internal static void XlAutoFree(IntPtr pXloper)
        {
            Debug.Print("In Managed XlAutoFree");
        }

        internal static void XlAutoFree12(IntPtr pXloper12)
        {
            Debug.Print("In Managed XlAutoFree12");
        }

        internal static IntPtr XlAddInManagerInfo(IntPtr pXloper)
        {
            Debug.Print("In Managed XlAddInManagerInfo");
            return IntPtr.Zero;
        }

        internal static IntPtr XlAddInManagerInfo12(IntPtr pXloper12)
        {
            Debug.Print("In Managed XlAddInManagerInfo12");
            return IntPtr.Zero;
        }

        // P{assing in from unmanaged code during initialization 
        static string pathXll;
        static IntPtr hModuleXll;

        static List<XlMethodInfo> registeredMethods = new List<XlMethodInfo>();
        static List<string> addedMenus = new List<string>();
        static List<XlMethodInfo> addedCommands = new List<XlMethodInfo>();

        // TODO: Improve the separation between the Xll registration and the 
        // assemblies being registered.
        // Functions that an XlLibrary must implement
        public static short AutoOpen()
        {
            short result = 0;
            try
            {
                // Clear any references, if we are already loaded
                UnregisterMethods();

                object xlCallResult;
                //XlCallImpl.TryExcelImpl(XlCallImpl.xlGetName, out xlCallResult);
                //xllPath = (string)xlCallResult;
                XlCallImpl.TryExcelImpl(XlCallImpl.xlcMessage, out xlCallResult /*Ignore*/ , true, "Registering library " + pathXll);

                // InitializeIntegration has loaded the DnaLibrary
                IntegrationHelpers.DnaLibraryAutoOpen();

                result = 1; // All is OK
            }
            catch (Exception e)
            {
                // TODO: What to do here?
                Debug.WriteLine(e.Message);
                result = 0;
            }
            finally
            {
                // Clear the status bar message
                object xlCallResult;
                XlCallImpl.TryExcelImpl(XlCallImpl.xlcMessage, out xlCallResult /*Ignore*/ , false);
            }

            return result;
        }

        internal static void AutoFree(IntPtr pXloper)
        {
            // CONSIDER: This might be improved....
            // Another option would be to have the Com memory allocator run in unmanaged code.
            // Right now I think this is OK, and easiest from where I'm coming.
            // This function can only be called after a return from a user function.
            // I just free all the possibly big memory allocations.

            XlObjectArrayMarshaler.FreeMemory();
        }

        public static void RegisterMethods(List<MethodInfo> methods)
        {
            List<XlMethodInfo> xlMethods = XlMethodInfo.ConvertToXlMethodInfos(methods);

            xlMethods.ForEach(RegisterXlMethod);
        }

        private static void RegisterXlMethod(XlMethodInfo mi)
        {
            // TODO: Store the handle (but no unregistration for now)
            int index = registeredMethods.Count;
            SetJump(index, mi.FunctionPointer);
            String procName = String.Format("f{0}", index);

            string functionType = mi.ReturnType == null ? "" : mi.ReturnType.XlType.ToString();
            string argumentNames = "";
            bool showDescriptions = false;
            string[] argumentDescriptions = new string[mi.Parameters.Length];

            for (int j = 0; j < mi.Parameters.Length; j++)
            {
                XlParameterInfo pi = mi.Parameters[j];

                functionType += pi.XlType;
                if (j > 0)
                    argumentNames += ", ";
                argumentNames += pi.Name;
                argumentDescriptions[j] = pi.Description;

                if (pi.Description != "")
                    showDescriptions = true;

                // DOCUMENT: Here is the patch for the Excel Function Description bug.
                // DOCUMENT: I add ". " to the last parameters.
                if (j == mi.Parameters.Length - 1)
                    argumentDescriptions[j] += ". ";

            } // for each parameter

            if (mi.IsVolatile)
                functionType += "!";
            // TODO: How do these interact ?
            // DOCUMENT: If # is set and there is an R argument, 
            // Excel considers the function volatile
            // You can call xlfVolatile, false in beginning of function to clear.
            if (mi.IsMacroType)
                functionType += "#";

            // DOCUMENT: Here is the patch for the Excel Function Description bug.
            // DOCUMENT: I add ". " if the function takes no parameters.
            string functionDescription = mi.Description;
            if (mi.Parameters.Length == 0)
                functionDescription += ". ";

            if (mi.Description != "")
                showDescriptions = true;

            // DOCUMENT: When there is no description, we don't add any.
            // This allows the user to work around the Excel bug where an extra parameter is displayed if
            // the function has no parameter but displays a description
            int numArguments;
            // DOCUMENT: Maximum 20 Argument Descriptions when registering using Excel4 function.
            int numArgumentDescriptions;
            if (showDescriptions)
            {
                numArgumentDescriptions = Math.Min(argumentDescriptions.Length, 20);
                numArguments = 10 + numArgumentDescriptions;
            }
            else
            {
                numArgumentDescriptions = 0;
                numArguments = 9;
            }

            object[] registerParameters = new object[numArguments];
            registerParameters[0] = pathXll;
            registerParameters[1] = procName;
            registerParameters[2] = functionType;
            registerParameters[3] = mi.Name;
            registerParameters[4] = argumentNames;
            registerParameters[5] = mi.IsCommand ? 2 /*macro*/
                                                          : (mi.IsHidden ? 0 : 1); /*function*/
            registerParameters[6] = mi.Category;
            registerParameters[7] = mi.ShortCut; /*shortcut_text*/
            registerParameters[8] = mi.HelpTopic; /*help_topic*/ ;

            if (showDescriptions)
            {
                registerParameters[9] = functionDescription;

                for (int k = 0; k < numArgumentDescriptions; k++)
                {
                    registerParameters[10 + k] = argumentDescriptions[k];
                }
            }

            // Basically suppress problems here !?
            try
            {
                object xlCallResult;
                XlCallImpl.TryExcelImpl(XlCallImpl.xlfRegister, out xlCallResult, registerParameters);
                mi.RegisterId = (double)xlCallResult;
                registeredMethods.Add(mi);
            }
            catch (Exception e)
            {
                // TODO: What to do here?
                Debug.WriteLine(e.Message);
            }

            if (mi.IsCommand)
            {
                RegisterMenu(mi);
            }
        }

        private static void RegisterMenu(XlMethodInfo mi)
        {
            if (   mi.MenuName != null && mi.MenuName != ""
                && mi.MenuText != null && mi.MenuText != "")
            {
                IntegrationHelpers.AddCommandMenu(mi.Name, mi.MenuName, mi.MenuText, mi.Description, mi.ShortCut, mi.HelpTopic);
            }
        }

        private static void UnregisterMethods()
        {
            object xlCallResult;

            // Remove menus
            IntegrationHelpers.RemoveCommandMenus();
            
            // Now take out the methods
            foreach (XlMethodInfo mi in registeredMethods)
            {
                try
                {
                    if (mi.IsCommand)
                    {
                        XlCallImpl.TryExcelImpl(XlCallImpl.xlfSetName, out xlCallResult, mi.Name, "");
                    }
                    else
                    {
                        // I follow the advice from X-Cell website
                        // to get function out of Wizard
                        XlCallImpl.TryExcelImpl(XlCallImpl.xlfRegister, out xlCallResult, pathXll, "xlAutoRemove", "J", mi.Name, Missing.Value, 0);
                    }
                    XlCallImpl.TryExcelImpl(XlCallImpl.xlfUnregister, out xlCallResult, mi.RegisterId);
                }
                catch (Exception e)
                {
                    // TODO: What to do here?
                    Debug.WriteLine(e.Message);
                }
            }
            registeredMethods.Clear();
        }

        internal static short AutoClose()
        {
            IntegrationHelpers.DnaLibraryAutoClose();
            // UnregisterMethods();
            return 1; // 0 if problems ?
        }

        internal static short AutoAdd()
        {
            return 1; // 0 if problems ?
        }

        internal static short AutoRemove()
        {
            // Apparently better if called here, 
            // so I try to, but make it safe to call again.
            UnregisterMethods();
            return 1; // 0 if problems ?
        }

        internal static IntPtr AddInManagerInfo(IntPtr pXloperAction)
        {
            ICustomMarshaler m = XlObjectMarshaler.GetInstance("");
            object action = m.MarshalNativeToManaged(pXloperAction);
            object result;
            if ((action is short && (short)action == 1) ||
                (action is double && (double)action == 1))
            {
                result = IntegrationHelpers.DnaLibraryGetName();
            }
            else
                result = IntegrationMarshalHelpers.GetExcelErrorObject(IntegrationMarshalHelpers.ExcelError_ExcelErrorValue);
            return m.MarshalManagedToNative(result);
        }
    }
}

