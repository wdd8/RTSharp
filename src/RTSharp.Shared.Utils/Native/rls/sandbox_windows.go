package main

import (
	"log"
	"os"
	"syscall"
	"unsafe"
)

func sandbox() {
	const tokenIntegrityLevel = 25
	const seGroupIntegrity = 0x20

	os.Clearenv()

	var token syscall.Token
	if err := syscall.OpenProcessToken(syscall.Handle(^uintptr(0)), syscall.TOKEN_ADJUST_DEFAULT, &token); err != nil {
		log.Fatal(err)
	}
	defer token.Close()

	sid, err := syscall.StringToSid("S-1-16-0")
	if err != nil {
		log.Fatal(err)
	}

	label := struct {
		sid        *syscall.SID
		attributes uint32
	}{sid, seGroupIntegrity}

	r, _, err := syscall.NewLazyDLL("advapi32.dll").NewProc("SetTokenInformation").Call(
		uintptr(token), tokenIntegrityLevel, uintptr(unsafe.Pointer(&label)), unsafe.Sizeof(label)+uintptr(syscall.GetLengthSid(sid)))
	if r == 0 {
		log.Fatal(err)
	}
}
