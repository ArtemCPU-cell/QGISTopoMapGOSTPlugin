[xml]$x = Get-Content -Raw -Encoding UTF8 'C:\Users\artem\source\repos\OsmToShapeFile\out\test2\test2.qgs'
Write-Host 'ID linkage check:'
foreach ($m in $x.qgis.projectlayers.maplayer) {
  $n = $m.layername
  $mid = $m.id
  $tid = ($x.qgis.SelectSingleNode("//layer-tree-layer[@name=`"$n`"]")).id
  $ok = if ($mid -eq $tid) { 'OK' } else { 'MISMATCH' }
  Write-Host "  $n : map=$mid  tree=$tid  -> $ok"
}
Write-Host ''
Write-Host "provider ogr: $($x.qgis.projectlayers.maplayer[0].provider.OuterXml)"
Write-Host ''
Write-Host "first <maplayer> first 250 chars:"
Write-Host $x.qgis.projectlayers.maplayer[0].OuterXml.Substring(0,250)