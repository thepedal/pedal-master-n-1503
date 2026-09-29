// Sandbox harness: drives Pedal Master N's Work() on raw float32 stereo files.
// usage: harness in.f32 out.f32 meters.txt sr block P=V ...
#define __declspec(x)
#define __cdecl
#include <vector>
#include <string>
#include <cstdlib>
#include <cstring>
#include "../native/PedalMasterN.cpp"

void CMachineDataOutput::Write(void *, int) {}
void CMachineDataInput::Read(void *, int) {}
struct Out : CMachineDataOutput { using CMachineDataOutput::Write; std::vector<char> b; void Write(void *p, int n) override { b.insert(b.end(), (char*)p, (char*)p + n); } };
struct In  : CMachineDataInput  { using CMachineDataInput::Read; std::vector<char> b; size_t pos = 0; void Read(void *p, int n) override { memcpy(p, &b[pos], n); pos += n; } };

int main(int argc, char **argv)
{
    FILE *fi = fopen(argv[1], "rb"); std::vector<float> x;
    float f; while (fread(&f, 4, 1, fi) == 1) x.push_back(f); fclose(fi);
    int sr = atoi(argv[4]), blk = atoi(argv[5]);
    CMasterInfo info = {}; info.SamplesPerSec = sr;
    mi m; m.pMasterInfo = &info;
    std::vector<std::pair<int,int>> later;   // "@frame:P=V" changes
    for (int a = 6; a < argc; a++) {
        std::string s = argv[a]; int at = -1;
        if (s[0] == '@') { size_t c = s.find(':'); at = atoi(s.substr(1, c - 1).c_str()); s = s.substr(c + 1); }
        size_t e = s.find('='); int p = atoi(s.substr(0, e).c_str()), v = atoi(s.substr(e + 1).c_str());
        if (at < 0) m.v[p] = v; else later.push_back({at, p * 100000 + v});
    }
    FILE *fm = fopen(argv[3], "w");
    int frames = x.size() / 2;
    for (int i = 0; i < frames; i += blk) {
        for (auto &c : later) if (c.first >= i && c.first < i + blk) m.v[c.second / 100000] = c.second % 100000;
        int n = frames - i < blk ? frames - i : blk;
        m.Work(&x[2 * i], n, WM_READWRITE);
        // Poll slot 1 about every 33 ms, like the GUI.
        if ((i / blk) % (int)(0.033 * sr / blk + 1) == 0 || i + blk >= frames) {
            In rq; int id = 1, slot = 1; rq.b.resize(8); memcpy(&rq.b[0], &id, 4); memcpy(&rq.b[4], &slot, 4);
            Out o; m.HandleGUIMessage(&o, &rq);
            In r; r.b = o.b; int ver, rate, lat, B, dr; float fv[11];
            r.Read(ver); r.Read(rate); r.Read(lat); for (int k = 0; k < 11; k++) r.Read(fv[k]); r.Read(B); r.Read(dr);
            fprintf(fm, "R %d %d %d %d", i, ver, rate, lat); for (int k = 0; k < 11; k++) fprintf(fm, " %.9g", fv[k]);
            fprintf(fm, " %d %d", B, dr);
            for (int k = 0; k < B; k++) { float e; r.Read(e); fprintf(fm, " b%.9g", e); }
            float c3[3]; for (int k = 0; k < 3; k++) { r.Read(c3[k]); fprintf(fm, " c%.9g", c3[k]); }
            fprintf(fm, "\n");
        }
    }
    fclose(fm);
    FILE *fo = fopen(argv[2], "wb"); fwrite(x.data(), 4, x.size(), fo); fclose(fo);
    printf("latency %d\n", m.Latency());
}
