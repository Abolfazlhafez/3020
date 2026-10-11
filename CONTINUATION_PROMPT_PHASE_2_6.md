Continue FoxyVPN from Phase 2.6. Preserve the Android project and its behavior. Active Windows source version is 2.6.0. This phase added strict DoH HTTP response-header parsing and test source but did not run final tests.

Next coding tasks:
1. Review HTTP/2 response-header semantics: `:status` cardinality/order, regular-header duplicates, informational responses, stream errors, and session failure propagation. Avoid broad refactors without tests.
2. Audit SOCKS5 UDP association shutdown and query task cleanup; confirm the UDP relay only answers the exact associated loopback client and is cancellation-safe.
3. Review DNS response truncation (`TC`), DNS-over-TCP framing limits, and whether retry policy is bounded and query-correlated. Never add direct DNS/network fallback.
4. Audit route/firewall transaction journaling, crash recovery and kill-switch behavior; document unsupported cases rather than weakening protection.
5. Reconcile CI artifact paths and notices. Keep Android sources unchanged.
6. Continue coding without running the comprehensive test suite until implementation is complete, as requested. At final stage run Windows restore/build/test, native ABI/dependency checks, portable publish and Inno Setup, then real Windows 10/11 tunnel, auth, DNS/IP leak, route rollback and reboot recovery acceptance tests. Do not claim any test passed unless logs show it.
