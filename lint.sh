#!/usr/bin/env bash
# Lints first-party C# (Assets/Scripts, Assets/Editor) against .editorconfig. Exit code 0 = clean.
# Needs the Unity-generated Assembly-CSharp*.csproj (open the project in Unity once to create them).
set -u
cd "$(dirname "$0")"
rc=0
for proj in Assembly-CSharp Assembly-CSharp-Editor; do
  out=$(dotnet build "$proj.csproj" --no-restore -v q 2>&1 | grep -E ' (error|warning) ' | sort -u)
  [ -n "$out" ] && { echo "$out"; rc=1; }
done
dotnet format whitespace Assembly-CSharp.csproj --verify-no-changes --no-restore --include Assets/Scripts/ Assets/Editor/ || rc=1
dotnet format style Assembly-CSharp.csproj --verify-no-changes --no-restore --severity warn --include Assets/Scripts/ Assets/Editor/ || rc=1
exit $rc
