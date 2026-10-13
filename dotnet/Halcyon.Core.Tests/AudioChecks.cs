// The C# audio port (unity/Assets/Halcyon/Core/Audio) against the web
// version's renders (tools/dev/audio-ref.ts): engine loops of five cars,
// every bed, one-shot and UI sound, and the first bars of each radio station.
// Lengths, loop rpms, song titles and flags must match exactly; samples
// within 1e-4 (both sides store float32; the runtimes' sin and exp may
// differ in the last bit).
using System;
using System.Collections.Generic;
using System.Text.Json;
using Halcyon.Core.Audio;

namespace Halcyon.Core.Tests
{
    static class AudioChecks
    {
        const double Tol = 1e-4;

        static void Samples(Check c, JsonElement head, float[] data, string where)
        {
            int n = head.GetArrayLength();
            if (n > data.Length)
            {
                c.Fail($"{where}: {data.Length} samples, the reference has at least {n}");
                return;
            }
            int i = 0;
            foreach (JsonElement v in head.EnumerateArray())
            {
                int k = i++;
                c.Near(RefJson.D(v), data[k], Tol, () => $"{where}[{k}]");
            }
        }

        static CarSpec Car(string id)
        {
            foreach (var s in Cars.All) if (s.id == id) return s;
            throw new ArgumentException("no car " + id);
        }

        public static IEnumerable<Check> All(JsonElement root)
        {
            int sr = (int)RefJson.D(root, "sampleRate");
            var list = new List<Check>();

            foreach (JsonElement e in root.GetProperty("engines").EnumerateArray())
            {
                string id = RefJson.S(e, "car");
                var c = new Check("audio", "engine " + id);
                var set = EngineSynth.RenderSet(Car(id), sr);
                c.Bool(e.GetProperty("electric").GetBoolean(), set.voice.electric, "electric");
                foreach (var (key, loops) in new[] { ("on", set.on), ("off", set.off) })
                {
                    var refs = e.GetProperty(key);
                    if (refs.GetArrayLength() != loops.Length)
                    {
                        c.Fail($"{key}: {loops.Length} loops, expected {refs.GetArrayLength()}");
                        continue;
                    }
                    int i = 0;
                    foreach (JsonElement l in refs.EnumerateArray())
                    {
                        var loop = loops[i];
                        string w = $"{key}[{i}]";
                        c.Num(RefJson.D(l, "rpm"), loop.rpm, w + ".rpm");
                        c.Num(RefJson.D(l, "length"), loop.data.Length, w + ".length");
                        c.Near(RefJson.D(l, "rms"), Dsp.Rms(loop.data), Tol, () => w + ".rms");
                        Samples(c, l.GetProperty("head"), loop.data, w);
                        i++;
                    }
                }
                list.Add(c);
            }

            {
                var c = new Check("audio", "beds");
                foreach (JsonElement b in root.GetProperty("beds").EnumerateArray())
                {
                    string id = RefJson.S(b, "id");
                    var bed = (Bed)Enum.Parse(typeof(Bed), id, true);
                    var d = Beds.Render(bed, sr);
                    c.Num(RefJson.D(b, "length"), d.Length, id + ".length");
                    c.Near(RefJson.D(b, "rms"), Dsp.Rms(d), Tol, () => id + ".rms");
                    Samples(c, b.GetProperty("head"), d, id);
                }
                list.Add(c);
            }

            {
                var c = new Check("audio", "one-shots and UI sounds");
                foreach (JsonElement s in root.GetProperty("shots").EnumerateArray())
                {
                    string id = RefJson.S(s, "id");
                    int v = (int)RefJson.D(s, "variant");
                    var d = Beds.Render((Shot)Enum.Parse(typeof(Shot), id, true), sr, v);
                    c.Num(RefJson.D(s, "length"), d.Length, id + ".length");
                    c.Near(RefJson.D(s, "rms"), Dsp.Rms(d), Tol, () => id + ".rms");
                    Samples(c, s.GetProperty("head"), d, id);
                }
                foreach (JsonElement u in root.GetProperty("ui").EnumerateArray())
                {
                    string id = RefJson.S(u, "id");
                    var d = Beds.Render((UiSound)Enum.Parse(typeof(UiSound), id, true), sr);
                    c.Num(RefJson.D(u, "length"), d.Length, "ui." + id + ".length");
                    c.Near(RefJson.D(u, "rms"), Dsp.Rms(d), Tol, () => "ui." + id + ".rms");
                    Samples(c, u.GetProperty("head"), d, "ui." + id);
                }
                list.Add(c);
            }

            foreach (JsonElement st in root.GetProperty("radio").EnumerateArray())
            {
                string id = RefJson.S(st, "station");
                var c = new Check("audio", "radio " + id);
                var radio = new RadioStation((StationId)Enum.Parse(typeof(StationId), id, true), sr, (long)RefJson.D(st, "seed"));
                int i = 0;
                foreach (JsonElement b in st.GetProperty("bars").EnumerateArray())
                {
                    var bar = radio.RenderBar();
                    string w = $"bar {i}";
                    c.Num(RefJson.D(b, "length"), bar.l.Length, w + ".length");
                    c.Str(RefJson.S(b, "title"), bar.title, w + ".title");
                    c.Str(RefJson.S(b, "artist"), bar.artist, w + ".artist");
                    c.Bool(b.GetProperty("newSong").GetBoolean(), bar.newSong, w + ".newSong");
                    c.Bool(b.GetProperty("ident").GetBoolean(), bar.ident, w + ".ident");
                    c.Near(RefJson.D(b, "rmsL"), Dsp.Rms(bar.l), Tol, () => w + ".rmsL");
                    c.Near(RefJson.D(b, "rmsR"), Dsp.Rms(bar.r), Tol, () => w + ".rmsR");
                    Samples(c, b.GetProperty("headL"), bar.l, w + ".l");
                    Samples(c, b.GetProperty("headR"), bar.r, w + ".r");
                    i++;
                }
                list.Add(c);
            }
            return list;
        }
    }
}
