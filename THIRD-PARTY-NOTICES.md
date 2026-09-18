# Third-Party Notices

The spike has no runtime package dependencies outside the .NET runtime.

Test dependencies:

| Package | Version | Licence | Source |
|---|---:|---|---|
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |

Transitive test-only packages:

| Package family | Licence | Source |
|---|---|---|
| Microsoft.CodeCoverage and Microsoft.TestPlatform.* | MIT | https://github.com/microsoft/vstest |
| Newtonsoft.Json | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| xunit.abstractions, xunit.analyzers, xunit.assert, xunit.core and xunit.extensibility.* | Apache-2.0 | https://github.com/xunit/xunit |

Resolved versions and integrity hashes are recorded in each project's
`packages.lock.json`. The runtime projects currently have no third-party NuGet
dependencies.

The Brother QL-800 Windows driver is proprietary software installed separately
by the operator. It is not included in or linked as a package dependency of this
project.
