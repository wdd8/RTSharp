package main

import (
	"encoding/json"
	"os"

	"github.com/autobrr/rls"
)

func main() {
	sandbox()

	in := json.NewDecoder(os.Stdin)
	out := json.NewEncoder(os.Stdout)

	var name string
	for in.Decode(&name) == nil {
		r := rls.ParseString(name)

		out.Encode(map[string]any{
			"Type":    r.Type.String(),
			"Artist":  r.Artist,
			"Title":   r.Title,
			"Year":    r.Year,
			"Series":  r.Series,
			"Episode": r.Episode,
		})
	}
}
