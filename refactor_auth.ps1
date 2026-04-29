$mappings = @{
    "ImportController" = "ModuleKeys.Import"
    "InventoryAdjustmentsController" = "ModuleKeys.Inventory"
    "InventoryAuditsController" = "ModuleKeys.InventoryCount"
    "InventoryOpeningBalanceController" = "ModuleKeys.InventoryOpening"
    "MappingSeederController" = "ModuleKeys.Mapping"
    "InventoryIntelligenceController" = "ModuleKeys.ReportsMain"
    "ImagesController" = "ModuleKeys.Settings"
    "InstallmentsController" = "ModuleKeys.AccountingMain"
    "FixedAssetsController" = "ModuleKeys.Assets"
    "ExportController" = "ModuleKeys.ReportsMain"
    "DashboardKpiController" = "ModuleKeys.Dashboard"
    "CouponsController" = "ModuleKeys.Coupons"
    "CustomerCategoryController" = "ModuleKeys.Customers"
    "CashierPerformanceController" = "ModuleKeys.ReportsMain"
    "BrandController" = "ModuleKeys.Brands"
    "BarcodeController" = "ModuleKeys.Barcode"
    "AuthController" = "ModuleKeys.Staff"
    "AccountingControllers" = "ModuleKeys.AccountingMain"
}

$files = Get-ChildItem -Path d:\Sportive.API\Controllers -Filter *.cs

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    $controllerName = $file.BaseName
    if ($mappings.ContainsKey($controllerName)) {
        $moduleKey = $mappings[$controllerName]
        $origContent = $content
        
        # If it's a Report Controller, requireEdit is always false
        if ($controllerName -match "Report|Profitability|DashboardKpi|InventoryIntelligence|Export|CashierPerformance") {
            $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[Authorize\(Roles\s*=[^\]]+\]', "[RequirePermission($moduleKey)]")
        } else {
            $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[Authorize\(Roles\s*=\s*"[^"]*?(Staff|Cashier|Accountant)[^"]*"\)\]', "[RequirePermission($moduleKey)]")
            $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[Authorize\(Roles\s*=\s*"[^"]*?(Admin,Manager|Admin)[^"]*"\)\]', "[RequirePermission($moduleKey, requireEdit: true)]")
            $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[Authorize\(Roles\s*=\s*AppRoles\.Admin\)\]', "[RequirePermission($moduleKey, requireEdit: true)]")
            $content = [System.Text.RegularExpressions.Regex]::Replace($content, '\[Authorize\(Roles\s*=\s*AppRoles\.AdminOrManager\)\]', "[RequirePermission($moduleKey, requireEdit: true)]")
        }

        if ($content -cne $origContent) {
            # Add using if missing
            if ($content -notmatch 'using Sportive\.API\.Attributes;') {
                $content = "using Sportive.API.Attributes;`r`n" + $content
            }
            if ($content -notmatch 'using Sportive\.API\.Models;') {
                $content = "using Sportive.API.Models;`r`n" + $content
            }
            Set-Content $file.FullName -Value $content -Encoding UTF8
            Write-Host "Updated $($file.Name)"
        }
    }
}
