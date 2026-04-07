#!/usr/bin/env bash

set -euo pipefail

dotnet test "DropAndForget.Tests/DropAndForget.Tests.csproj" --configuration Release
