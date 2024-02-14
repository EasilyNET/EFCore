# Taken from psake https://github.com/psake/psake

<#
.SYNOPSIS
  This is a helper function that runs a scriptblock and checks the PS variable $lastexitcode
  to see if an error occcured. If an error is detected then an exception is thrown.
  This function allows you to run command-line programs without having to
  explicitly check the $lastexitcode variable.
.EXAMPLE
  exec { svn info $repository_trunk } "Error executing SVN. Please verify SVN command-line client is installed"
#>
function Exec
{
    [CmdletBinding()]
    param(
        [Parameter(Position=0,Mandatory=1)][scriptblock]$cmd,
        [Parameter(Position=1,Mandatory=0)][string]$errorMessage = ($msgs.error_bad_command -f $cmd)
    )
    & $cmd
    if ($lastexitcode -ne 0) {
        throw ("Exec: " + $errorMessage)
    }
}

$artifacts = ".\artifacts"

if(Test-Path $artifacts) { Remove-Item $artifacts -Force -Recurse }

exec { & dotnet clean -c Release }
exec { & dotnet build -c Release }
exec { & dotnet test -c Release --no-build -l trx --verbosity=normal }

# Core
exec { & dotnet pack .\src\EasilyNET.SourceGenerator.Share\EasilyNET.SourceGenerator.Share.csproj -c Release -o $artifacts --include-symbols -p:SymbolPackageFormat=snupkg --no-build }

# EntityFramework Core
exec { & dotnet pack .\src\EasilyNET.Core.Domain.SourceGenerator\EasilyNET.Core.Domain.SourceGenerator.csproj -c Release -o $artifacts --include-symbols -p:SymbolPackageFormat=snupkg --no-build }
exec { & dotnet pack .\src\EasilyNET.Core.Domains\EasilyNET.Core.Domains.csproj -c Release -o $artifacts --include-symbols -p:SymbolPackageFormat=snupkg --no-build }
exec { & dotnet pack .\src\EasilyNET.EntityFrameworkCore\EasilyNET.EntityFrameworkCore.csproj -c Release -o $artifacts --include-symbols -p:SymbolPackageFormat=snupkg --no-build }

# Framework
exec { & dotnet pack .\src\EasilyNET.AutoInjection.SourceGenerator\EasilyNET.AutoInjection.SourceGenerator.csproj -c Release -o $artifacts --include-symbols -p:SymbolPackageFormat=snupkg --no-build }