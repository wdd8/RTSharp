$mainDirPath = $args[0];
$pluginDirPath = $args[1];

Write-Output $pluginDirPath

$mainDlls = Get-ChildItem -Path $mainDirPath -Filter "*.dll";

foreach ($pluginFile in Get-ChildItem -Path $pluginDirPath -Filter "*.dll") {
    $pluginFileVer = (Get-Item -Path $pluginFile.FullName).VersionInfo.FileVersion;
    $mainFile = ($mainDlls | Where-Object Name -eq $pluginFile.Name);

    if ($mainFile -ne $null -and $mainFile.VersionInfo.FileVersion -eq $pluginFileVer) {
        Write-Output "Removing $($pluginFile.Name)";
        Remove-Item $pluginFile.FullName;
    }
}

Function OtherExts {
  Param(
    $Input
  )

  $Input | Where-Object { $_.Name -like "*.pdb" -or $_.Name -like "*.exe" -or $_.Name -like "*.runtimeconfig.json" -or $_.Name -like "*.so" }
}

Function AlwaysDelete {
  Param(
    $Input
  )

  $Input | Where-Object { $_.Name -like "*.deps.json" -or $_.Name -eq "RTSharp" }
}

$mainOther = Get-ChildItem -Path $mainDirPath | OtherExts;

foreach ($pluginFile in Get-ChildItem -Path $pluginDirPath | OtherExts) {
    $mainFile = ($mainOther | Where-Object Name -eq $pluginFile.Name);

    if ($mainFile -ne $null) {
        Write-Output "Removing $($pluginFile.Name)";
        Remove-Item $pluginFile.FullName;
    }
}

foreach ($pluginFile in Get-ChildItem -Path $pluginDirPath | AlwaysDelete) {
    Write-Output "Removing $($pluginFile.Name)";
    Remove-Item $pluginFile.FullName;
}