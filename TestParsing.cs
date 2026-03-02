using System;
using System.Collections.Generic;

class Test {
    static void Main() {
        var lines = new[] {
            "0\t0\t0\t0\t0\t0\tReplace\t0\tWindshield Urethane Kit\t0\t1\t50\t0\t0\t0\t0.8\t0\t0",
            "0\t0\t0\t0\t0\tReplace\t0\tWindshield Glass Primer\t0\t1\t25\t0\t0\t0\t0.5\t0\t0"
        };
        
        var opTypes = new[] { "Rpr", "Replace", "R&I", "R+I", "Blend", "Refinish", "O/H", "Sublet", "Add", "Remove", "Install", "Repair" };
        
        foreach (var line in lines) {
            Console.WriteLine($"\n=== Parsing: {line.Substring(0, Math.Min(60, line.Length))}... ===");
            var parts = line.Split('\t');
            Console.WriteLine($"Parts count: {parts.Length}");
            
            // Find operation
            int opIndex = -1;
            string operation = "";
            for (int i = 0; i < parts.Length; i++) {
                var val = parts[i].Trim();
                foreach (var op in opTypes) {
                    if (val.Equals(op, StringComparison.OrdinalIgnoreCase)) {
                        opIndex = i;
                        operation = val;
                        break;
                    }
                }
                if (opIndex >= 0) break;
            }
            Console.WriteLine($"Operation: '{operation}' at index {opIndex}");
            
            // Find description
            string description = "";
            int descIndex = -1;
            for (int i = opIndex + 1; i < parts.Length; i++) {
                var val = parts[i].Trim();
                if (string.IsNullOrEmpty(val) || val == "0") continue;
                if (decimal.TryParse(val, out _)) continue;
                if (val.Length > 2) {
                    description = val;
                    descIndex = i;
                    break;
                }
            }
            Console.WriteLine($"Description: '{description}' at index {descIndex}");
            
            // Find numbers after description
            var numbersAfterDesc = new List<decimal>();
            for (int i = descIndex + 1; i < parts.Length; i++) {
                var val = parts[i].Trim();
                if (decimal.TryParse(val, out decimal num) && num != 0) {
                    numbersAfterDesc.Add(num);
                }
            }
            Console.WriteLine($"Numbers after description: [{string.Join(", ", numbersAfterDesc)}]");
            
            // Parse result
            string qty = "1", price = "", labor = "";
            if (numbersAfterDesc.Count >= 2) {
                qty = numbersAfterDesc[0].ToString("0");
                labor = numbersAfterDesc[numbersAfterDesc.Count - 1].ToString("0.0");
                if (numbersAfterDesc.Count >= 3) {
                    price = numbersAfterDesc[1].ToString("0.00");
                }
            }
            Console.WriteLine($"RESULT: Op={operation}, Desc={description}, Qty={qty}, Price={price}, Labor={labor}");
        }
    }
}
