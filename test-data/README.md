# Test data

`synthetic-dtm-epsg4326.tif` is a tiny, uncompressed, single-band float32
GeoTIFF used to verify the local DTM path without network access. It covers the
Moscow sample bbox and has EPSG:4326 GeoTIFF keys.

Example:

```powershell
dotnet run -- --offline --dem-file .\test-data\synthetic-dtm-epsg4326.tif `
  --bbox 55.75,37.60,55.77,37.64 --scale 25k --output .\out\dtm-test
```
