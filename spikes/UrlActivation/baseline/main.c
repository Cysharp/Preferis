// Baseline for spike 1: a C program with no runtime at all, launched by Launch Services.
// Logs the time from kernel process start to main() so we can separate LS/dyld overhead
// from .NET NativeAOT startup.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/sysctl.h>
#include <sys/time.h>
#include <unistd.h>

int main(void) {
    struct timeval now;
    gettimeofday(&now, NULL);
    struct kinfo_proc kp;
    size_t len = sizeof(kp);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid() };
    sysctl(mib, 4, &kp, &len, NULL, 0);
    struct timeval st = kp.kp_proc.p_starttime;
    double ms = (now.tv_sec - st.tv_sec) * 1000.0 + (now.tv_usec - st.tv_usec) / 1000.0;
    char path[1024];
    snprintf(path, sizeof path, "%s/Library/Logs/PreferisSpike/baseline.log", getenv("HOME"));
    FILE *f = fopen(path, "a");
    if (f) { fprintf(f, "c-main-entered  +%7.1f ms from process start\n", ms); fclose(f); }
    return 0;
}
