#!/usr/bin/env bash

set -e

rm -rf TestResults Coverage

dotnet test KUKULCAN.SharedKernel.Database.slnx \
  --configuration Release \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults \
  --logger "console;verbosity=normal"

reportgenerator \
  "-reports:TestResults/**/coverage.cobertura.xml" \
  "-targetdir:Coverage" \
  "-reporttypes:Cobertura"

mv Coverage/Cobertura.xml Coverage/coverage-cobertura.xml

echo
echo "Coverage generado:"
echo "Coverage/coverage-cobertura.xml"
