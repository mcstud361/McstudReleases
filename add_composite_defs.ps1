$jsonPath = 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\Data\Definitions.json'
$content = Get-Content $jsonPath -Raw

# New definitions to add
$newDefs = @'
    },
    {
      "id": "carbon-fiber-cfrp",
      "term": "Carbon Fiber-Reinforced Plastic (CFRP)",
      "category": "Composite Materials",
      "status": "SPECIAL PROCEDURES",
      "pPageLocation": "Materials > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Carbon Fiber-Reinforced Plastics (CFRPs) are Fiber-Reinforced Plastics (FRP) where the fibers are made of carbon. These are lightweight, high-strength materials used in performance and luxury vehicles.",
      "details": "CFRPs are used for body panels, structural reinforcements, drive shafts, suspension parts, and even passenger cells. Woven carbon fiber is stronger and lighter than chopped carbon fiber. Carbon fiber dust is conductive and can short circuit electrical tools. Raw carbon fiber can cause galvanic corrosion when in contact with metal, but this is eliminated once embedded in resin. All repairs must follow OEM procedures.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "smc-sheet-molded-compound",
      "term": "Sheet Molded Compound (SMC)",
      "category": "Composite Materials",
      "status": "SPECIAL PROCEDURES",
      "pPageLocation": "Materials > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Sheet Molded Compound (SMC) is smooth on both sides with no visible fibers unless the part is cracked. SMC parts cannot be repaired using fiberglass repair materials or plastic welders.",
      "details": "DO NOT use fiberglass repair materials with SMC because of the fillers and mold release agents that cause adhesion problems. SMC requires both sides of the part to be finish sanded. SMC is used for weight reduction and complex shapes. When repairing SMC: use composite repair adhesive, not fiberglass resin. Products: Fibre Glast Epoxy Resin, Evercoat Fibertech (Gorilla hair), Dynatron Fiberglass Filler. Allow proper dry time. Add primer at 25% of repair unit as refinish.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "frp-fiber-reinforced-plastic",
      "term": "Fiber-Reinforced Plastic (FRP)",
      "category": "Composite Materials",
      "status": "SPECIAL PROCEDURES",
      "pPageLocation": "Materials > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "FRP is a general term for Fiber-Reinforced Plastics representing any fiber a composite contains. Composites can contain glass fibers or carbon fibers.",
      "details": "FRP Backing can be made of fiberglass cloth or backing tape. General recommendation is 1/2 inch to 2 inch overlap on all sides of damage. Traditional fiberglass is made by saturating fiberglass cloth or mat with polyester resin. Fiberglass parts usually start with a gel coat applied to a waxed mold, then fiberglass and resin are hand or spray applied. Repairs can be done using polyester resin and fiberglass cloth or mat.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "composite-repair-safety",
      "term": "Composite Repair Safety Requirements",
      "category": "Composite Materials",
      "status": "REQUIRED",
      "pPageLocation": "Safety > Composites",
      "pPageRef": "OSHA, OEM",
      "pPageSystem": "All Systems",
      "definition": "When working with composites, proper safety equipment is critical. Fractured or splintered composites, particularly carbon fiber, can be extremely sharp.",
      "details": "Required PPE: Leather gloves when damaged fibers are exposed. NIOSH P100 combination respirator when grinding or cutting carbon fiber (prevents fibers and gases from lungs). Safety glasses. Composite dust from sanding, cutting, or grinding is a lung and eye irritant. Perform repairs in properly ventilated area. Review product-specific Safety Data Sheets (SDS) for each material used.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "composite-one-sided-repair",
      "term": "Composite One-Sided Cosmetic Repair",
      "category": "Composite Materials",
      "status": "PROCEDURE",
      "pPageLocation": "Repair Procedures > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "One-sided cosmetic repairs for composites primarily include scratches or gouges that do not penetrate through to the backside.",
      "details": "Steps: 1) Clean the repair area. 2) Scuff or sand the damaged area. 3) Apply composite repair adhesive or filler. 4) Sand to contour after product has cured. 5) Finish with paint maker's recommended grit for repair. Do not use solvents as fibers can wick solvents into the FRP.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "composite-two-sided-repair",
      "term": "Composite Two-Sided Repair",
      "category": "Composite Materials",
      "status": "PROCEDURE",
      "pPageLocation": "Repair Procedures > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Two-sided repairs include cracks, tears, or punctures that go through to the backside or have damage to the fibers.",
      "details": "Steps: 1) Remove any loose or damaged fibers with air saw or die grinder (cut a circle around damaged area). 2) Taper the front side of the repair. 3) Apply aluminum tape to front side to prevent material falling through. 4) Apply composite repair adhesive to backside repair area. 5) Press backing material (fiberglass cloth or tape, 1/2-2 inch overlap) into adhesive with firm pressure. 6) Apply another layer of adhesive over backing, covering all edges. 7) Allow to cure, remove aluminum tape. 8) Sand front side creating repair taper. 9) Apply adhesive and finish. Take care not to damage fibers. Fibers can wick solvents - avoid solvent use.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "pyramid-patch-repair",
      "term": "Pyramid Patch Composite Repair",
      "category": "Composite Materials",
      "status": "PROCEDURE",
      "pPageLocation": "Repair Procedures > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "A pyramid patch is an alternative method of composite repair involving a patch built up with alternate layers of repair product and fiber cloth or mesh.",
      "details": "Steps: 1) Clean and scuff backside repair area, vacuum dust. 2) Apply aluminum tape to front side. 3) Apply composite repair adhesive to backside backing, cover entire patch and allow to cure. 4) Remove aluminum tape from front, sand creating repair taper. 5) Apply layer of adhesive, then cloth/mesh, then another layer of adhesive. 6) Continue building layers in pyramid fashion. 7) Sand to contour after curing. Note: Some manufacturers have special instructions for their composite repair adhesive.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "carbon-fiber-challenges",
      "term": "Carbon Fiber Repair Challenges",
      "category": "Composite Materials",
      "status": "REFERENCE",
      "pPageLocation": "Materials > Carbon Fiber",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Carbon fiber presents unique repair challenges that may make replacement necessary instead of repair.",
      "details": "Cannot repair if damage extends to an edge. Cannot duplicate exposed carbon fiber weave if cracked (you cannot recreate the exact weave pattern of a tool-faced surface). Cannot buff if damage goes through clearcoat on tool-faced surfaces. Carbon fiber dust can short circuit electrical tools (conductive material). Raw carbon fiber causes galvanic corrosion with metal contact. Manufacturing uses autoclave process: 300°F heat and 100 psi pressure with epoxy, vinyl ester, polyester, or nylon resins.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "carbon-fiber-failure-types",
      "term": "Carbon Fiber/Composite Failure Types",
      "category": "Composite Materials",
      "status": "REFERENCE",
      "pPageLocation": "Damage Analysis > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Several types of failure can occur with composite materials that require different inspection and repair approaches.",
      "details": "DELAMINATION: One ply separates from another ply. May result from low-energy impact or manufacturing defect. Sometimes visible if near surface. DISBONDING: A ply separates from dissimilar material or core material. CORE DAMAGE: Damage to core material of part, can happen along with any other damage type. CRACKING/PUNCTURES: High energy impacts crack or puncture parts - easiest failure type to see. Inspect backside for secondary damage. TAP TEST: Technician taps across suspected damage area listening for tone change, marks where tone changes to identify damage boundaries.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "carbon-fiber-identification",
      "term": "Carbon Fiber Identification",
      "category": "Composite Materials",
      "status": "REFERENCE",
      "pPageLocation": "Materials > Carbon Fiber",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Identifying carbon fiber parts is important as repair procedures differ significantly from other materials.",
      "details": "CHOPPED CARBON FIBER: Looks similar to fine fibered fiberglass with very fine fibers in random directions. Extremely thin, light, and stronger than fiberglass. May look similar to SMC but contains carbon fibers. WOVEN CARBON FIBER: Lighter and stronger than chopped. Distinctive weave pattern visible. Structural and non-structural parts can be carbon fiber. Not limited to body panels - can include drive shafts, suspension parts, passenger cell, structural reinforcements, engine/transmission parts. PAINTED PANELS: May be easy to miss - use same identification steps as traditional composites.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "composite-repair-options",
      "term": "Composite/Carbon Fiber Repair Options",
      "category": "Composite Materials",
      "status": "PROCEDURE",
      "pPageLocation": "Repair Procedures > Composites",
      "pPageRef": "OEM Procedures",
      "pPageSystem": "OEM Specific",
      "definition": "Several repair options exist for carbon fiber and composites depending on damage type and OEM procedures.",
      "details": "COSMETIC/BUFFING: Surface scratches and damage that can be buffed out easily. COMPOSITE-TYPE REPAIR: Uses same products and techniques as SMC repairs (pyramid patch). Note: This can ONLY be used for cosmetic applications because repairs are weaker than carbon fiber and do not meet same performance standards. VACUUM BAGGING: Advanced repair method with many different options and steps. Consult OEM procedures. Always follow original equipment manufacturer (OEM) procedures. Repairs act as secondary bond depending on repair materials adhering to original materials - use materials designed for the specific composite being repaired.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "composite-repair-products",
      "term": "Composite Repair Products",
      "category": "Composite Materials",
      "status": "REFERENCE",
      "pPageLocation": "Materials > Composites",
      "pPageRef": "Product Reference",
      "pPageSystem": "All Systems",
      "definition": "Specific products recommended for composite and SMC/carbon fiber repairs.",
      "details": "RESIN: Fibre Glast Epoxy Resin & Epoxy Cure. FIBERGLASS: Fiberglass Mat (white bag, red letters). GORILLA HAIR: Evercoat Fibertech 100633 - Reinforced Repair Compound Filler. FILLER: Dynatron Fiberglass Filler. PRIMER: Add at 25% repair unit as refinish unit, treat same as other parts. STRUCTURAL ADHESIVES (GM approved): Fusor 2098, 3M 07333, Pilogrip 5770P, SEM 39757 Structural Impact Resistant Adhesive. Always verify current OEM approved products list.",
      "degInquiry": null,
      "degResponse": null,
      "links": {}
    },
    {
      "id": "stage-secure-carbon-fiber",
      "term": "Stage and Secure Carbon Fiber for Refinish",
      "category": "Composite Materials",
      "status": "NOT INCLUDED",
      "pPageLocation": "Refinishing Procedures > Composites",
      "pPageRef": "DEG",
      "pPageSystem": "CCC/MOTOR, Mitchell, Audatex",
      "definition": "Stage and secure carbon fiber molding for refinish is a non-included operation per DEG inquiry.",
      "details": "Carbon fiber parts require special handling and staging for refinish operations. This includes proper positioning, securing, and preparation specific to carbon fiber substrates. Also consider: DE-NIB, Wet/Dry Sand, Rub-Out and Buff for carbon fiber parts.",
      "degInquiry": "DEG Inquiry",
      "degResponse": "Non-included operation",
      "links": {}
    }
  ]
}
'@

# Find the last closing brace pattern and replace
$pattern = '\}\s*\]\s*\}\s*$'
$content = $content -replace $pattern, $newDefs

# Save the file
$content | Out-File -FilePath $jsonPath -Encoding UTF8 -NoNewline
Write-Host "Added carbon fiber/composite definitions to Definitions.json"
