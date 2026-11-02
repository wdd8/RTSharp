//go:build !windows

package main

import (
	"log"
	"os"
	"syscall"
	"time"
)

func sandbox() {
	const nobody = 65534

	os.Clearenv()

	if os.Geteuid() == 0 {
		if err := syscall.Setgroups(nil); err != nil {
			log.Fatal(err)
		}
		if err := syscall.Setgid(nobody); err != nil {
			log.Fatal(err)
		}
		if err := syscall.Setuid(nobody); err != nil {
			log.Fatal(err)
		}
	}

	// The runtime opens its poller on the first timer and crashes if it can't, so that has to happen before the limit
	time.Sleep(time.Nanosecond)

	if err := syscall.Setrlimit(syscall.RLIMIT_NOFILE, &syscall.Rlimit{}); err != nil {
		log.Fatal(err)
	}
}
