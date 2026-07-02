package resources

import (
	"bufio"
	_ "embed"
	"strings"
)

//go:embed api-list.txt
var nativeAPIList string

//go:embed spec-list.txt
var nativeSpecList string

func NativeAPIList() []string {
	return ParseAPIList(nativeAPIList)
}

func NativeSpecList() []string {
	return ParseAPIList(nativeSpecList)
}

func ParseAPIList(value string) []string {
	scanner := bufio.NewScanner(strings.NewReader(value))
	lines := []string{}
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		lines = append(lines, line)
	}
	return lines
}
