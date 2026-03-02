using System;
using System.Diagnostics;
using McStudDesktop.Services;

namespace McStudDesktop
{
    /// <summary>
    /// Simple test class to verify Excel integration is working
    /// Run this before launching the full UI to test Excel backend
    /// </summary>
    public static class TestExcelIntegration
    {
        public static void RunTests()
        {
            Debug.WriteLine("=== Excel Integration Test ===");

            try
            {
                // Test 1: Initialize engine
                Debug.WriteLine("\n[Test 1] Initializing ExcelEngineService...");
                var engine = new ExcelEngineService();
                engine.Initialize();
                Debug.WriteLine("[Test 1] ✓ Initialized successfully");

                // Test 2: Set battery type to Single
                Debug.WriteLine("\n[Test 2] Setting battery type to 'Single'...");
                engine.SetInput("SOPList_A29", "Single");
                engine.Calculate();
                var summary1 = engine.GetSOPListSummary();
                Debug.WriteLine($"[Test 2] Results:");
                Debug.WriteLine($"  - Total Operations: {summary1.TotalOperations}");
                Debug.WriteLine($"  - Total Price: ${summary1.TotalPrice}");
                Debug.WriteLine($"  - Total Labor: {summary1.TotalLabor} hrs");
                Debug.WriteLine($"  - Total Refinish: {summary1.TotalRefinish} hrs");

                // Test 3: Change battery type to Dual
                Debug.WriteLine("\n[Test 3] Changing battery type to 'Dual'...");
                engine.SetInput("SOPList_A29", "Dual");
                engine.Calculate();
                var summary2 = engine.GetSOPListSummary();
                Debug.WriteLine($"[Test 3] Results:");
                Debug.WriteLine($"  - Total Operations: {summary2.TotalOperations}");
                Debug.WriteLine($"  - Total Price: ${summary2.TotalPrice}");
                Debug.WriteLine($"  - Total Labor: {summary2.TotalLabor} hrs");
                Debug.WriteLine($"  - Total Refinish: {summary2.TotalRefinish} hrs");

                // Test 4: Enable ADAS
                Debug.WriteLine("\n[Test 4] Enabling ADAS...");
                engine.SetInput("SOPList_C29", "Yes");
                engine.Calculate();
                var summary3 = engine.GetSOPListSummary();
                Debug.WriteLine($"[Test 4] Results:");
                Debug.WriteLine($"  - Total Operations: {summary3.TotalOperations}");
                Debug.WriteLine($"  - Total Price: ${summary3.TotalPrice}");
                Debug.WriteLine($"  - Total Labor: {summary3.TotalLabor} hrs");

                // Test 5: Change vehicle type to EV
                Debug.WriteLine("\n[Test 5] Changing vehicle type to 'EV'...");
                engine.SetInput("SOPList_A35", "EV");
                engine.Calculate();
                var summary4 = engine.GetSOPListSummary();
                Debug.WriteLine($"[Test 5] Results:");
                Debug.WriteLine($"  - Total Operations: {summary4.TotalOperations}");
                Debug.WriteLine($"  - Total Price: ${summary4.TotalPrice}");
                Debug.WriteLine($"  - Total Labor: {summary4.TotalLabor} hrs");

                // Test 6: Get operations list
                Debug.WriteLine("\n[Test 6] Reading operations list...");
                var ops = engine.GetOperations("SOP List", 29, 171, "O", "V", "R");
                Debug.WriteLine($"[Test 6] Found {ops.Count} operations");
                if (ops.Count > 0)
                {
                    Debug.WriteLine($"  - First operation: {ops[0].Name} (Labor: {ops[0].Labor} hrs, Price: ${ops[0].Price})");
                }

                // Test 7: Reset to defaults
                Debug.WriteLine("\n[Test 7] Resetting to defaults...");
                engine.ResetToDefaults();
                var summary5 = engine.GetSOPListSummary();
                Debug.WriteLine($"[Test 7] Results after reset:");
                Debug.WriteLine($"  - Total Operations: {summary5.TotalOperations}");
                Debug.WriteLine($"  - Total Price: ${summary5.TotalPrice}");

                // Cleanup
                engine.Dispose();

                Debug.WriteLine("\n=== ✓ All Tests Passed ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\n=== ✗ Test Failed ===");
                Debug.WriteLine($"Error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
