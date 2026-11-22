# Security model and responsible disclosure

USB Guardian is an educational Windows security inspection prototype; it is **not an antivirus or complete endpoint protection product**.

The Windows Host process may read filename and attribute metadata, a bounded file prefix (2 KiB), and up to 24 MiB of file data to compute SHA-256 through a read-only stream. This does not remove risk from filesystem driver bugs, race conditions or malicious data that exploit a host parser.

SafeLab Analyzer is a separate process launched via Windows AppContainer security capabilities, **without declared network capabilities**. It runs under a Job Object with memory (256 MiB) and time (10 seconds) bounds. It is handed only up to 2 KiB of Base64-encoded bytes and an ACL-granted result path; it is NOT given a source USB path. It explicitly checks the TokenIsAppContainer bit before parsing bytes and refuses to run outside this context. There is no automatic lower-isolation fallback.

The host writes a temporary JSON result file under LocalAppData and deletes it after analysis. This does not guarantee forensic erasure. Analyze only files you are authorized to inspect.

AppContainer + Job Object is **not a virtual machine** and cannot be advertised as zero-risk isolation. Independent review should validate ACLs, worker token, network restrictions, privilege inheritance, memory limits, timeout handling and user data access on Windows Home before publishing an installer.

To report a vulnerability before public publication, open a private security advisory in the GitHub repository after it is created. Do not post exploit samples containing third-party data in public issues.
