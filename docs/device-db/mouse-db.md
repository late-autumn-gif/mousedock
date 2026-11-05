# 鼠标 VID/PID 与电量协议总表

生成：2026-10-30T23:01:55　条目：reader 35 / 商业版设备 26 / 上游库 318

三块来源：`notes/battery-readers.md`（商业版 MouseDock 的 battery_*.py 逆向，帧级细节；带「未确认」的就是真没反出来）、`extracted/devices.json`（商业版身份库 v143）、libratbag + OpenRazer + Solaar（上游开源库，只有型号↔VID:PID↔协议族，没有帧格式）。

## 一、已还原帧格式的读取器（35 条）

| kind | 品牌/设备 | VID:PID | 通道 | 请求帧 | 响应 | 电量字节 | 来源 |
|---|---|---|---|---|---|---|---|
| beast_x | WLMouse Beast X（模块没硬写 VID/PID，只查 caps=report4、in/out 64） | 无硬编码 | OutputReport 0x04 → InputReport 0x04 | `04 LL LL 1A 06 00…`（64B）；LL = CRC16(poly 0xA001, init 0xFFFF) over `packet[3:32]`，小端写 [1:3] | 64 | `data[8]`，0–100 直接用 | battery-readers.md |
| bykc_sc525 | AULA/INPHIC SC525（0x32C2:0x0027，release 257，iface 2，UP 0xFF0F/usage 1） | 32c2:0027 | OutputReport 0x0E（16B）→ InputReport 0x0E（32B） | `0E 20` + 14×`00` | 32 | `raw[8] & 0x7F`；`raw[8]&0x80`→charging；`raw[9]&0x80`==0→sleep | battery-readers.md |
| bykc_sc900 | 0x093A:0x522C（release 257，iface 3，UP 0xFF05/usage 1） | 093a:522c | 纯被动 InputReport 0x09 | 不发帧 | 33（也收 8 字节截断帧） | `raw[1] & 0x7F`；`raw[1]&0x80`→charging | battery-readers.md |
| ausdom_g03 | AUSDOM G03 Pro 8K（0x363C:0xFF99，release 544，iface 1，UP 0xFFA0/usage 2） | 363c:ff99 | OutputReport 0x0B（64B）→ InputReport 0x0B（64B） | 电量 `0B 13` + 62×`00`；连接态 `0B 14` + 62×`00` | 64 | `raw[18]`（0–100）；`raw[17]&1`→charging；`raw[20]`==1 视为已连接 | battery-readers.md |
| dareu_a980pro | DAREU A980 PRO（用 device_db.match_family_rule + receiver_group，没硬写 VID/PID） | 无硬编码 | Feature 0x0A（65B） | `0A 00 03 07 80 00 00` + 58×`00` | 65 | `b[8]`；`b[8]==0`→NotOnline；`b[7] ∈ {1,2}`→charging；`b[1]&0x0F` 是状态(0/1) | battery-readers.md |
| exx_alpha7 | EXX Alpha 7（profile 走 match_family_rule('exx_zhanli')） | 无硬编码 | report 0x00，65B（hidapi 带报告 ID，响应剥首字节） | 登录 `00 00 00 01 08 EF 09 B0 8A EE C8 3D 7C`；设备信息 `00 1E 00 01 00`，命令∈{0x00,0x1E} | 65→剥首字节 64 | 状态通知（0xD1）：`b[4]`=online、`b[5]`=电量、`b[6]`=charging（exx.notification）；设备信息里的电量偏移未确认 | battery-readers.md |
| finalmouse_ulx | Finalmouse ULX（0x361D:0x0100，UP 0xFF00） | 361d:0100 | Output tx=0x04 → Input rx=0x05，64B | `<tx> 02 <0x80\|cmd> 00` + 60×`00` | 64 | cmd=38：payload[0]（payload=`b[4:4+size]`）0–100；cmd=5：payload[0:2] 小端 mV（2500–4600）查表插值 3000→0.2%、3620→5、3660→10、3740→25、3880→50、4170→75、4380→100（voltage_percent） | battery-readers.md |
| finalmouse_slx | SLX 接收器 0x0300/0x0301、SLX 有线 0x0310/0x0311（同 VID） | 无硬编码 | Output tx=0x01 → Input rx=0x02，64B | 同上，只换报告 ID | 64 | 同上 | battery-readers.md |
| gwolves_getbatper | G-Wolves Feature64 产品线 | 无硬编码 | Feature 0x00，65B | `00 00 00 02 02 00 83` + 58×`00` | 65→剥首字节 64 | `b[7]`；`b[6]`∈{0,1} 充电标志 | battery-readers.md |
| gwolves_old | 同上，HTS Ultra 的 getOldBattery 注册路径 | 无硬编码 | Feature 0x00，65B | `00 00 02 8F 01` + 60×`00` | 65→剥首字节 64 | `b[5]`；`b[4]`∈{0,1} 充电标志 | battery-readers.md |
| compx | ATK 系（0x373B:0x12E9/0x10C9/0x101B）、LAMZU Crystal（0x3554:0xF50D，release 529）、G-Wolves 共用接收器（0x33E4:0x3854） | 373b:12e9, 3554:f50d, 33e4:3854 | Output 0x08（17B）→ Input 0x08 | `08 <route> 00… 49`（17B，末字节 0x49）；ATK 走 protocols.request('compx') | 17 | `b[6]` 百分比，`b[7]&1`→charging | battery-readers.md |
| infinity100_d6 | AM Infinity .100（AM30 转发，0x3151:0x5007，release 8，iface 2，UP 0xFFFF/usage 2） | 3151:5007 | Feature（65B，命令字节当报告 ID） | `D6` + 6×`00` + 校验 + 补零到 64；`body[7] = 255 - sum(body[:7])%256`（infinity100.packet） | 64 | `b[3]`；`b[2]==0`→sleep；`b[2]==0xFF`→无效 | battery-readers.md |
| infinity97_am35 | AM35 接收器（0x0E8D:0x0703，UP 0xFF14/usage 1，iface 1；release 775 走 input_profile） | 0e8d:0703 | Feature 0x14（65B）或 Output 0x06（62B） | `14 06 <route> 05 5A 02 00 <cmd lo> <cmd hi>` + 补零到 62/65 | 65 / 62 | RACE payload：`payload[2]`=电量、`payload[1]`=充电、`payload[4]==0`→sleep（infinity97.battery） | battery-readers.md |
| hidpp_1004 | Logitech 0x046D:0xC54F（UP 0xFF43，usage 0x0302，iface 2） | 046d:c54f | HID++ 长报文 Output/Input 0x11，20B | `11 01 <featIdx> <(func<<4)\|tag> <params…>` 补零到 20B（logitech_x3.request） | 20 | 先 `call(0,0,(16,4))` 发现 feature 0x1004，索引=响应[4]；再统一电池 getStatus：`level=响应[4]`、`state=响应[6]`，state∈{1,2,4}→charging | battery-readers.md |
| mchose_mhub | MCHOSE K7 V2 / A5 V3 / A7 V3 Ultra+ / K5（0x3837，PID 表在 mchose_v2_profiles；UP 0xFF01/0xFF0B） | 无硬编码 | report 0x4D，64B 双向 | `4D 01 01 00 00 09 00 00 08` + 55×`00` | 64 | `body=b[8:]`；`body[12]`=百分比；`body[11]`∈{0,1} 充电状态；body[0:4]=VID/PID，须命中 0x3837+PID 表 | battery-readers.md |
| pulsar_cmouse | Pulsar（0x3710，UP 0xFF02/usage 2，iface 1） | 无硬编码 | Output 0x08（17B）→ Input 0x08（17B） | `08 <cmd> 00 00 00 00 <hold> … <chk>`，17B；cmd∈{3,4}，cmd==4 时 hold 必须为 0；`b[16]=(0x55-sum(b[:16]))&0xFF`（request） | 17 | 电压 mV 组合 `b[7]`/`b[8]`（高低位未确认）→电压表 3050/3420/…/4200 阶梯；充电取 `b[10]` 的位（read 里 6/7/8/9/10 一组常量） | battery-readers.md |
| rapoo_vt2max | Rapoo VT2 MAX（0x24AE:0x1423，release 261，iface 1，UP 0xFF00/usage 2） | 24ae:1423 | 纯被动 InputReport 0x07 | 不发帧 | 13 | `b[8]`；`b[7]`：0→sleep、1→online、2→charging；`b[1]&0x0F`∈{0,1,2} 是帧类型 | battery-readers.md |
| rapoo_aa | Rapoo（0x24AE:0x14A5，iface 1，UP 0xFF00，usage 2/14） | 24ae:14a5 | Output 0x06（33B）→ Input 0x07（19B） | `06 A5 AA` + 30×`00`（read.sample） | 19（也收 20） | 走 protocols.decode('rapoo')：`b[7]` 状态(0/1/2)、`b[8]` 百分比 | battery-readers.md |
| rawm_hub | RAWM（VID 0x1915，UP 0xFF00/0xFFFF） | 无硬编码 | report 0x00，65B 发；输入 64B 分片重组 | `00 [C0] 8D 01 0D 03 00 00 <ts 8B 小端>` 补零到 64；channel 0 多一个 0xC0 | 64/片 | 响应是 JSON 文本，电量取字段 `battery`、充电取 `chr`（rawm.exchange / query_info） | battery-readers.md |
| razer_carbon | Razer DeathAdder V4 Pro Carbon Fiber 接收器（0x1532:0x00F0，release 257，iface 3，UP 0x0C/usage 1） | 1532:00f0 | Feature 0x5B（91B） | `00 00 <tx> 00 00 00 02 07 5B` + 补零；`b[89]=XOR(b[3:89])`，tx 轮转 0..30 | 91 | `响应[10]`；`响应[1]`：0/1=未就绪（继续等）、2=成功、其它=失败 | battery-readers.md |
| ridlag_owl | RIDLAG Owl X 8K Dongle（0x362D:0xFF09，UP 0xFFC1/usage 1，iface 1） | 362d:ff09 | Output 0xB5（21B）列表 / 0xB3（62B）电量 → Input 21/64 | 列表 `B5 03` + 19×`00`；电量 `B3 06` + 60×`00` | 21 / 64 | `b[20] & 0x7F`；`b[20]&0x80`→charging | battery-readers.md |
| rog_8k | ROG SPEEDNOVA 8K 鼠标集合（match_family_rule('rog_8k')，无硬写 VID） | 无硬编码 | report 0x03，21B 或 65B | `03 12 07 00 00` + 补零到 21/65 | 21 / 65 | `b[5]`（0→sleep）；`b[10]`∈{0,1} 充电标志 | battery-readers.md |
| rog_standard | ROG 标准无编号接口（read_rog_standard） | 无硬编码 | Feature 无编号 report 0x00，65B | `00 12 07` + 62×`00` | 65（hidapi 可能去掉首字节 0 → 64） | `b[2:4]`=`12 07`；`b[4]`=电量（0→sleep）；`b[9]`≠0→charging | battery-readers.md |
| rog_omni | ROG（decode_rog，64B 布局） | 无硬编码 | report 0x03，64B | `03 12 07` + 61×`00` | 64 | `b[5]` 电量（0→sleep）；`b[10]`≠0→charging | battery-readers.md |
| angrymiao | Angry Miao 鼠标 | 无硬编码 | report 0xF7，64B | `F7` + 63×`00`（MIAO_REQUEST） | 64 | `b[0]==0xF7`→电量 `b[2]`；`b[0]==0 && b[1]==0xF7`→电量 `b[3]`；0→unresponsive | battery-readers.md |
| scyrox_scenter | SCYROX（0x395A，PID∈{4,10,12,14}，UP 0xFF00/usage 2，iface 2） | 无硬编码 | report `<rid>`，64B 双向 | `<rid> 2E 00 03` + 60×`00`，`b[63]=sum(b[:63])&0xFF` | 64 | `b[4]` 电量；`b[5]`≠0→charging；`b[6]==0`→sleep；`b[62]` 须为 1；设备 ID=`b[12:14]\|b[14:16]<<16` | battery-readers.md |
| sora_v3 | NinjaForce PixArt Sora V3（0x093A:0xEB02 / 0xE010，UP 0xFF00/FF01/FF02） | 093a:eb02 | Feature 0x06（16B，PID 0xE010 时 9B） | `06 <cmd> 00 00 01` + 11×`00`，cmd∈{17,18,19} | 16 或 9 | 按命令查表（17/18/19 ↔ charging/battery/connection 三个字段），映射表没在反汇编里展开，字节偏移未确认 | battery-readers.md |
| steelseries_aerox | SteelSeries Aerox 接收器（模块无 VID 常量） | 无硬编码 | report 0xD2，64B | `D2` + 63×`00` | 64（也收 65 带前缀 0） | `b[1]&0x7F`=step(1–21) → `(step-1)*5` 百分比；`b[1]&0x80`→charging；step==0→sleep | battery-readers.md |
| vgn_kc2 | VGN F1 V5 Turbo+（0x391D:0x1A23，release 263，iface 1，UP 0xFFC1/usage 1） | 391d:1a23 | Output 0xB5（62B）/0xB3 → Input | 连接 `B5 03` + 60×`00`；设备信息 `B3 06` + 62×`00` | 21 / 64 | `b[20] & 0x7F`；`b[20]&0x80`→charging | battery-readers.md |
| vgn_ld | VGN V HUB ldMs（0x391D:0x102E/0x1A2A/0x1037/0x1A29，UP 0xFFA0/usage 2） | 391d:102e | report 0xA0，33 或 64B | `A0 02 25` + 补零到 33/64 | 33 / 64 | `b[3]` 百分比（percent 校验 0–100）；充电状态标记 `vgn_ld_charging_unknown`（不做猜测） | battery-readers.md |
| zynoo_gp1 | ZYNOO GP1（0x3886:0xCA02–0xCA04/0xCBFE/0xCBFF，UP 0xFF80/usage 1） | 3886:ca02 | report 0x00，17B | `00 9A 00 00 08` + 12×`00` | 17→剥首字节 16 | `b[10] & 0x7F`；`b[10]&0x80`→charging；`b[11]==0`→sleep | battery-readers.md |
| waizowl_ogm | WAIZOWL OGM 8K（0x0728 共用 dongle，靠回包里的设备 ID 认鼠标） | 无硬编码 | report 0x00，65B | `00 <cmd> 00 81 01` + 补零 64，`b[63]=(0xA1-sum(b[:63]))&0xFF`，再前置一个 `00`（request） | 65→剥首字节 64 | `b[6]` 百分比（decode_battery） | battery-readers.md |
| webbrands_f83 | lunafury / attackshark / lamzu / ko_one / wlmouse（FEATURE83 家族） | 无硬编码 | Feature 0x83，65B | 65B：`b[3]=target`（0 或 2）、`b[4]=02`、`b[6]=83`，其余 0（request） | 65 | 回包 `(b[0],b[1],b[4],b[6])==(0,0xA1,2,0x83)`；`b[8]`=电量、`b[7]`=充电（decode）；`b[1]==0xA0`→sleep | battery-readers.md |
| webbrands_ipi | ipi_pix 家族 | 无硬编码 | Output 0x03（64B） | `03 00 50 00 02 4F 81` + 补零，`b[1]=(255-sum(b[2:]))&0xFF`（request） | 64 | 未确认（decode 的 ipi_pix 分支只到 b[0]==3、b[4]==2 的校验） | battery-readers.md |
| webbrands_akko | akko 家族 | 无硬编码 | Output 0x04（64B） | `04 00 00 1A 06` + 59×`00`（request） | 未确认 | 未确认 | battery-readers.md |

## 二、商业版设备库（devices.json）

| 型号 | 名称 | VID:PID | usage | 电量族 | DPI | 已验证 |
|---|---|---|---|---|---|---|
| atkf1ultimate | ATK F1 Ultimate | 0x373B:0x1031 | 0xFF02/2 | compx | catalog | 否 |
| atkf1ultimate | ATK F1 Ultimate | 0x373B:0x102E | 0xFF02/2 | compx | catalog | 否 |
| atka9ultimate | ATK A9 Ultimate | 0x373B:0x11D9 | 0xFF02/2 | compx | catalog | 否 |
| atka9ultimate | ATK A9 Ultimate | 0x373B:0x11B6 | 0xFF02/2 | compx | catalog | 否 |
| vgnf2promax | VGN F2 Pro Max | 0x3554:0xFB3E | 0xFF02/2 | compx |  | 否 |
| vgnf2promax | VGN F2 Pro Max | 0x3554:0xFB3D | 0xFF02/2 | compx |  | 否 |
| z7 | 前行者 Z7 | 0x3554:0xF517 | 0xFF02/2 | compx | z7 | 是 |
| v12m | 塞洛斯 V12M | 0x395A:0x0006 | 0xFF00/2 | scyrox12 | scyrox | 是 |
| zero | ATK Zero | 0x373B:0x11D8 | 0xFF02/2 | compx | atk | 是 |
| f1v3 | ATK F1 V3 Ultimate+ | 0x373B:0x1278 | 0xFF02/2 | compx | atk | 是 |
| vt9s | 雷柏 VT9S V2 | 0x24AE:0x1453 | 0xFF00/2 | rapoo | rapoo | 是 |
| zero | ATK Zero | 0x373B:0x1155 | 0xFF05/1 | nordic54 |  | 否 |
| zero | ATK Zero | 0x373B:0x124F | 0xFF05/1 | nordic54 |  | 否 |
| zero | ATK Zero | 0x373B:0x1154 | 0xFF05/1 | nordic54 |  | 否 |
| sora | Sora V2 | 0x1915:0xAE8A | 0xFFA0/1 | sora | sora | 是 |
| sora | Sora V2 | 0x1915:0xAE1C | 0xFFA0/None | sora |  | 否 |
| sora | Sora V2 | 0x1915:0xAE11 | 0xFFA0/None | sora |  | 否 |
| sora_v2_8k | Sora V2 8K | 0x1915:0xAE8C | 0xFFA0/1 | sora |  | 否 |
| r1promax | VXE R1 Pro Max | 0x3554:0xF58A | 0xFF02/2 | compx | catalog | 否 |
| r1promax | VXE R1 Pro Max | 0x3554:0xF58C | 0xFF02/2 | compx | catalog | 否 |
| r1seplus | VXE R1 SE+ | 0x3554:0xF58E | 0xFF02/2 | compx | catalog | 否 |
| r1seplus | VXE R1 SE+ | 0x3554:0xF58F | 0xFF02/2 | compx | catalog | 否 |
| vgnf1pro | VGN F1 Pro | 0x3554:0xF503 | 0xFF02/2 | compx |  | 否 |
| vgnf1pro | VGN F1 Pro | 0x3554:0xF502 | 0xFF02/2 | compx |  | 否 |
| scyroxv8 | Scyrox V6 / V8 | 0x3554:0xF5F7 | 0xFF02/2 | compx |  | 否 |
| scyroxv8 | Scyrox V6 / V8 | 0x3554:0xF5F6 | 0xFF02/2 | compx |  | 否 |

## 三、商业版品牌/家族映射（brand_ids）

```json
{
 "lamzu_crystal_models": {
  "2": "LAMZU Atlantis Mini",
  "6": "LAMZU Atlantis Mini Champion Edition"
 },
 "bykc_models": {
  "0x00710068": "英菲克 元创·星隧",
  "0x00710037": "狼蛛 SC525"
 },
 "waizowl_8k": {
  "interface": {
   "vendor_id": 6421,
   "product_id": 1832,
   "release_number": 518,
   "interface_number": 3,
   "usage_page": 65290,
   "usage": 1
  },
  "shared": true,
  "verified": false,
  "status_models": {
   "f2070000": "WAI ZOWL OGM Cloud",
   "f4070000": "WAI ZOWL Cloud 3950",
   "f6070000": "WAI ZOWL Cloud XS",
   "f3070000": "WAI ZOWL OGM PRO V2",
   "f5070000": "WAI ZOWL OGM PRO V2",
   "b0070000": "WAI ZOWL OGM PRO"
  },
  "identity_source": "https://owlnest.waizowl.com/assets/js/index-BX2DrCSr.js"
 },
 "scyrox_identity": {
  "source_date": "2026-10-16",
  "sources": [
   "https://gaming.scyrox.net/assets/user-BRbKhDdb.js",
   "https://gaming.scyrox.net/assets/index-Dv3zWbj4.js"
  ],
  "status_models": {
   "395A:0003": "塞洛斯 V12L",
   "395A:0005": "塞洛斯 V12M",
   "395A:0007": "塞洛斯 V12L-U",
   "395A:0009": "塞洛斯 V12M-U",
   "395A:000B": "塞洛斯 V6S",
   "395A:000D": "塞洛斯 V8S"
  }
 },
 "atk_identity": {
  "source_version": "ATK HUB 3.2.28",
  "source_date": "2026-10-16",
  "sources": [
   "https://bpcdn.atkgear.com/hub-v3/production/3.2.28/static/filter-8vgh_F2N.js",
   "https://bpcdn.atkgear.com/hub-v3/production/3.2.28/static/filter-CO7zKClF.js"
  ],
  "compx_cid_mid": {
   "1,2": "ATK Duckbill Ultra",
   "1,3": "ATK A9 Ultimate",
   "1,4": "ATK ZERO EX",
   "1,5": "ATK Y9 Ultimate",
   "1,6": "ATK A9 Ultra Max 2.0",
   "1,7": "ATK F1 EXTREME 2.0",
   "1,8": "ATK F1 Ultimate 2.0",
   "1,9": "ATK X1 EXTREME 2.0",
   "1,10": "ATK X1 Ultimate 2.0",
   "1,11": "ATK X1 Ultra Max 2.0",
   "1,12": "ATK F1 Ultra Max 2.0",
   "1,13": "ATK F1 LEVIATAN 2.0",
   "1,15": "ATK A9 Ultra Max 2.0 Tri-Mode",
   "1,16": "ATK U2 Ultimate 2.0",
   "1,17": "ATK U2 Mini 2.0",
   "1,18": "ATK U2 Ultra Max 2.0",
   "1,19": "ATK Z1 Ultimate 2.0",
   "1,21": "ATK A9 Air",
   "1,22": "ATK X1 EXTREME GT",
   "1,23": "ATK Ghost EXTREME",
   "1,24": "ATK Ghost Ultimate",
   "1,25": "ATK Duckbill EXTREME",
   "1,26": "ATK ZERO",
   "1,27": "ATK A9 Mini",
   "1,28": "ATK A9 Mini",
   "1,30": "ATK X EXTREME",
   "1,31": "ATK A9 Mini+",
   "1,32": "ATK X1 Ultra Max 2.0",
   "1,33": "ATK F1 Ultra Max 2.0",
   "1,37": "ATK A9 Ultra Max 2.0 Tri-Mode",
   "1,38": "ATK A9 Ultra Max 2.0",
   "1,39": "ATK A9 Ultimate+",
   "1,40": "ATK A9 Air+",
   "1,42": "ATK X1 Air+",
   "1,45": "ATK X1 EXTREME 2.0+",
   "1,46": "ATK X1 Ultimate 2.0+",
   "1,47": "ATK ZERO EX+",
   "1,48": "ATK Ghost EXTREME+",
   "1,49": "ATK Ghost Ultimate+",
   "1,50": "ATK U2 Ultimate 2.0+",
   "1,51": "ATK U2 Mini 2.0+",
   "1,52": "ATK A9 Mini+",
   "1,53": "ATK A9 Ultimate+",
   "1,54": "ATK A9 Air+",
   "1,55": "ATK 幻影 Ultimate",
   "1,58": "ATK F1 V3 EXTREME+",
   "1,59": "ATK F1 Ultimate 2.0+",
   "1,60": "ATK F1 V3 LEVIATAN+",
   "1,61": "ATK X1 Air+",
   "1,62": "ATK F1 V3 Ultimate+",
   "1,63": "ATK F1 V3 Ultimate+",
   "1,64": "ATK F1 V3 Ultimate+",
   "1,66": "ATK Y9 Ultimate+",
   "1,67": "ATK Z1 Ultimate 2.0+",
   "1,71": "ATK A9 Wireless Charging",
   "1,75": "ATK A9 Ultimate Silent",
   "1,76": "ATK X1 Ultimate 2.0 Silent",
   "1,85": "ATK F1 V3 Ultimate",
   "1,87": "ATK X1 Ultra Max 2.0",
   "1,90": "ATK F1 V3 PIKACHU Edition",
   "2,1": "ATK Dragonfly F1 PRO",
   "2,2": "ATK Dragonfly F1 PRO",
   "2,3": "ATK Dragonfly F1 PRO MAX",
   "2,4": "ATK Dragonfly F1 PRO MAX",
   "2,5": "ATK Dragonfly F1 MOBA",
   "2,6": "ATK Dragonfly F1 MOBA",
   "2,19": "ATK Dragonfly F1S",
   "2,20": "ATK Dragonfly Dragonfly F1S",
   "2,33": "ATK Dragonfly F1 MOBA",
   "2,36": "ATK F1 PRO",
   "2,37": "ATK F1 PRO MAX",
   "2,38": "ATK X1 PRO",
   "2,39": "ATK X1 PRO MAX",
   "2,40": "ATK Z1 PRO",
   "2,41": "ATK Z1 PRO MAX",
   "2,42": "ATK F1 Ultimate",
   "2,43": "ATK X1 Ultra",
   "2,44": "ATK FIERCE X",
   "2,45": "ATK F1 Ultra",
   "2,46": "ATK X1 Ultimate",
   "2,48": "ATK Dragonfly F1 PRO MAX Elden",
   "2,50": "ATK F1 EXTREME",
   "2,52": "ATK Dragonfly F1 PRO MAX JOJO",
   "2,53": "ATK Z1 Ultra",
   "2,56": "ATK F1 LEVIATAN",
   "2,64": "ATK U2 MINI",
   "2,65": "ATK U2 Ultimate",
   "2,66": "ATK U2 Ultra",
   "2,70": "ATK X1 EXTREME",
   "2,76": "ATK X1 S NK",
   "2,77": "ATK X1 SE NK",
   "2,78": "ATK U2 Plus NK",
   "2,79": "ATK U2 NK",
   "2,83": "ATK A9 Plus NK",
   "2,84": "ATK A9 NK",
   "2,85": "ATK A9 Pro Max",
   "2,87": "ATK A9 Ultra",
   "2,88": "ATK A9 SE NK",
   "2,89": "ATK X1 Lite",
   "2,90": "ATK X1E",
   "2,92": "ATK U2 SE NK",
   "2,96": "ATK Y9 Plus NK",
   "2,97": "ATK Y9 NK",
   "2,98": "ATK Y9 SE NK",
   "2,99": "ATK A9",
   "2,100": "ATK U2 Pro Max",
   "2,104": "ATK Dragonfly F1 PRO",
   "2,105": "ATK Dragonfly F1 PRO MAX",
   "2,106": "ATK Dragonfly F1 MOBA",
   "2,107": "ATK A9 X",
   "2,108": "ATK Y9 PRO MAX",
   "2,109": "ATK U2",
   "2,115": "ATK A9 NB ULTRA",
   "2,120": "ATK A9 NB",
   "2,124": "ATK U2 Plus NK",
   "2,128": "ATK U2 Pro Max",
   "2,129": "ATK A9 NB ULTRA+",
   "2,132": "ATK X1 S NK"
  },
  "usb_names": {
   "3554:F502:FF04:0002": "ATK Dragonfly F1 PRO",
   "373B:1014:FF04:0002": "ATK F1 PRO",
   "373B:1015:FF04:0002": "ATK F1 PRO MAX",
   "373B:1016:FF04:0002": "ATK X1 PRO",
   "373B:1017:FF04:0002": "ATK X1 PRO MAX",
   "373B:1018:FF04:0002": "ATK Z1 PRO",
   "373B:1019:FF04:0002": "ATK Z1 PRO MAX",
   "373B:102E:FF04:0002": "ATK F1 Ultimate",
   "373B:102F:FF04:0002": "ATK X1 Ultra",
   "373B:1030:FF04:0002": "ATK FIERCE X",
   "373B:103C:FF04:0002": "ATK F1 Ultra",
   "373B:103D:FF04:0002": "ATK X1 Ultimate",
   "373B:1045:FF04:0002": "ATK F1 EXTREME",
   "373B:104A:FF04:0002": "ATK Z1 Ultra",
   "373B:105B:FF04:0002": "ATK F1 LEVIATAN",
   "373B:1086:FF04:0002": "ATK U2 MINI",
   "373B:1087:FF04:0002": "ATK U2 Ultimate",
   "373B:10AC:FF04:0002": "ATK U2 Ultra",
   "373B:10E9:FF04:0002": "ATK X1 EXTREME",
   "373B:10FD:FF04:0002": "ATK Y9 PRO MAX",
   "373B:1106:FF04:0002": "ATK X1 S NK",
   "373B:1107:FF04:0002": "ATK X1 SE NK",
   "373B:110D:FF04:0002": "ATK U2 Plus NK",
   "373B:110E:FF04:0002": "ATK U2 NK",
   "373B:1115:FF04:0002": "ATK A9 Plus NK",
   "373B:1116:FF04:0002": "ATK A9 NK",
   "373B:1117:FF04:0002": "ATK A9 Pro Max",
   "373B:1119:FF04:0002": "ATK A9 Ultra",
   "373B:1135:FF04:0002": "ATK A9 SE NK",
   "373B:113E:FF04:0002": "ATK X1 Lite",
   "373B:113F:FF04:0002": "ATK X1E",
   "373B:1149:FF04:0002": "ATK U2 SE NK",
   "373B:1154:FF05:0001": "ATK ZERO",
   "373B:1157:FF04:0002": "ATK A9",
   "373B:1158:FF04:0002": "ATK U2 Pro Max",
   "373B:115B:FF04:0002": "ATK Y9 Plus NK",
   "373B:115C:FF04:0002": "ATK Y9 NK",
   "373B:115D:FF04:0002": "ATK Y9 SE NK",
   "373B:11A0:FF04:0002": "ATK A9 X",
   "373B:11A6:FF04:0002": "ATK Duckbill Ultra",
   "373B:11AA:FF04:0002": "ATK U2",
   "373B:11B6:FF04:0002": "ATK A9 Ultimate",
   "373B:11CC:FF04:0002": "ATK ZERO EX",
   "373B:11CF:FF04:0002": "ATK Y9 Ultimate",
   "373B:11DB:FF05:0001": "ATK A9 Pro Max 2.0 NK",
   "373B:11DD:FF04:0002": "ATK A9 Ultra Max 2.0",
   "373B:11E3:FF04:0002": "ATK F1 EXTREME 2.0",
   "373B:11E4:FF04:0002": "ATK F1 Ultimate 2.0",
   "373B:11E5:FF04:0002": "ATK X1 EXTREME 2.0",
   "373B:11E7:FF04:0002": "ATK X1 Ultimate 2.0",
   "373B:11EC:FF04:0002": "ATK X1 Ultra Max 2.0",
   "373B:11ED:FF04:0002": "ATK F1 Ultra Max 2.0",
   "373B:11F0:FF04:0002": "ATK F1 LEVIATAN 2.0",
   "373B:11F2:FF04:0002": "ATK A9 Ultra Max 2.0 Tri-Mode",
   "373B:11F3:FF04:0002": "ATK A9 NB ULTRA",
   "373B:11F5:FF04:0002": "ATK U2 Ultimate 2.0",
   "373B:11F6:FF04:0002": "ATK U2 Mini 2.0",
   "373B:11F7:FF04:0002": "ATK U2 Ultra Max 2.0",
   "373B:1201:FF04:0002": "ATK Z1 Ultimate 2.0",
   "373B:120A:FF04:0002": "ATK A9 Air",
   "373B:120B:FF04:0002": "ATK X1 EXTREME GT",
   "373B:120D:FF04:0002": "ATK Ghost EXTREME",
   "373B:120E:FF04:0002": "ATK Ghost Ultimate",
   "373B:1211:FF05:0001": "ATK A9 Ultimate 3.0",
   "373B:1222:FF05:0001": "ATK A9 SE NK",
   "373B:1223:FF05:0001": "ATK A9 NK",
   "373B:1224:FF05:0001": "ATK A9 Plus NK",
   "373B:1230:FF04:0002": "ATK Duckbill EXTREME",
   "373B:123A:FF05:0001": "ATK Ghost Ultimate",
   "373B:1245:FF04:0002": "ATK A9 NB",
   "373B:124E:FF04:0002": "ATK ZERO",
   "373B:1251:FF05:0001": "ATK A9 Mini+",
   "373B:1256:FF04:0002": "ATK A9 Mini",
   "373B:125A:FF04:0002": "ATK A9 Mini",
   "373B:1263:FF05:0001": "ATK A9 Plus NK",
   "373B:1264:FF05:0001": "ATK A9 Pro Max 2.0 NK",
   "373B:1266:FF04:0002": "ATK X EXTREME",
   "373B:1268:FF05:0001": "ATK A9 Mini",
   "373B:1269:FF04:0002": "ATK A9 Mini+",
   "373B:126C:FF04:0002": "ATK U2 Plus NK",
   "373B:126D:FF04:0002": "ATK U2 Pro Max",
   "373B:126E:FF04:0002": "ATK F1 Ultra Max 2.0",
   "373B:126F:FF04:0002": "ATK A9 Ultra Max 2.0",
   "373B:1270:FF04:0002": "ATK A9 Ultra Max 2.0 Tri-Mode",
   "373B:1274:FF04:0002": "ATK X1 Ultra Max 2.0",
   "373B:127C:FF04:0002": "ATK X1 Air+",
   "373B:127E:FF04:0002": "ATK A9 Ultimate+",
   "373B:127F:FF04:0002": "ATK A9 Air+",
   "373B:1285:FF04:0002": "ATK X1 EXTREME 2.0+",
   "373B:1286:FF04:0002": "ATK X1 Ultimate 2.0+",
   "373B:1287:FF05:0001": "ATK A9 Ultimate+",
   "373B:1288:FF05:0001": "ATK A9 Air+",
   "373B:1289:FF04:0002": "ATK ZERO EX+",
   "373B:128A:FF04:0002": "ATK Ghost EXTREME+",
   "373B:128B:FF04:0002": "ATK Ghost Ultimate+",
   "373B:128C:FF04:0002": "ATK U2 Ultimate 2.0+",
   "373B:128D:FF04:0002": "ATK U2 Mini 2.0+",
   "373B:128E:FF04:0002": "ATK A9 Mini+",
   "373B:128F:FF05:0001": "ATK A9 Ultra Max 2.0+",
   "373B:1292:FF04:0002": "ATK A9 Ultimate+",
   "373B:1293:FF04:0002": "ATK A9 Air+",
   "373B:1296:FF04:0002": "ATK 幻影 Ultimate",
   "373B:1299:FF05:0001": "ATK X1 SE Nearlink",
   "373B:129B:FF04:0002": "ATK F1 V3 EXTREME+",
   "373B:129C:FF04:0002": "ATK F1 Ultimate 2.0+",
   "373B:129D:FF04:0002": "ATK F1 V3 LEVIATAN+",
   "373B:129E:FF05:0001": "ATK ZERO+",
   "373B:12A2:FF04:0002": "ATK X1 Air+",
   "373B:12A7:FF04:0002": "ATK F1 V3 Ultimate+",
   "373B:12A8:FF04:0002": "ATK F1 V3 Ultimate+",
   "373B:12A9:FF04:0002": "ATK F1 V3 Ultimate+",
   "373B:12AB:FF04:0002": "ATK A9 NB ULTRA+",
   "373B:12AF:FF04:0002": "ATK Y9 Ultimate+",
   "373B:12B0:FF04:0002": "ATK Z1 Ultimate 2.0+",
   "373B:12B3:FF04:0002": "ATK A9 Ultimate Silent",
   "373B:12B4:FF04:0002": "ATK X1 Ultimate 2.0 Silent",
   "373B:12C6:FF04:0002": "ATK A9 Wireless Charging",
   "373B:12CE:FF05:0001": "ATK 幻影 Ultimate",
   "373B:12D0:FF05:0001": "ATK A9 NB ULTRA",
   "373B:12D9:FF05:0001": "ATK X1 Ultimate 2.0+",
   "373B:12DE:FF05:0001": "ATK X1 Ultra Max 2.0+ Tri-Mode",
   "373B:12E3:FF05:0001": "ATK U2 Plus NK",
   "373B:12EC:FF05:0001": "ATK X1 Air+",
   "373B:12F1:FF05:0001": "ATK A9 Ultimate+",
   "373B:12FA:FF04:0002": "ATK F1 V3 Ultimate",
   "373B:12FF:FF04:0002": "ATK X1 Ultra Max 2.0",
   "373B:1300:FF05:0001": "ATK A9 Plus NK",
   "373B:1301:FF05:0001": "ATK A9 Pro Max 2.0 NK",
   "373B:1304:FF05:0001": "ATK F1 V3 Ultimate+",
   "373B:1305:FF05:0001": "ATK A9 3.0 NK",
   "373B:1307:FF05:0001": "ATK F1 V3 Ultra Max",
   "373B:1308:FF05:0001": "ATK F1 V3 Ultimate+",
   "373B:130E:FF05:0001": "ATK X1 SE NK",
   "373B:130F:FF05:0001": "ATK A9 SE 3.0 NK",
   "373B:1312:FF05:0001": "ATK 幻影 Ultimate",
   "373B:1314:FF05:0001": "ATK A9 Ultimate",
   "373B:131A:FF04:0002": "ATK X1 S NK",
   "373B:132A:FF04:0002": "ATK F1 V3 PIKACHU Edition",
   "373B:1331:FF05:0001": "ATK ZERO PLUS GENGAR EDITION",
   "373B:1337:FF05:0001": "ATK A9 Plus 3.0 NK",
   "373B:1338:FF05:0001": "ATK A9 Pro Max 3.0 NK",
   "373B:1343:FF05:0001": "ATK A9 Air",
   "373B:1344:FF05:0001": "ATK F1 V3 Ultimate",
   "373B:134D:FF05:0001": "ATK A9 SE 3.0 NK",
   "373B:134E:FF05:0001": "ATK A9 3.0 NK",
   "373B:134F:FF05:0001": "ATK A9 Plus 3.0 NK",
   "373B:1350:FF05:0001": "ATK A9 Pro Max 3.0 NK"
  }
 },
 "rapoo_identity": {
  "source_date": "2026-10-16",
  "sources": [
   "https://hub.rapoo.com/enc.data",
   "https://hub.rapoo.com/enc-map.data"
  ],
  "models": {
   "0x4610": "雷柏 VT0",
   "0x4611": "雷柏 VT3S",
   "0x4612": "雷柏 VT3",
   "0x4613": "雷柏 VT7",
   "0x4614": "雷柏 VT9",
   "0x4615": "雷柏 VT1",
   "0x4616": "雷柏 VT7MAX",
   "0x4617": "雷柏 VT3MAX",
   "0x4618": "雷柏 VT9MAX",
   "0x4619": "雷柏 VT7Air",
   "0x461A": "雷柏 VT3Air",
   "0x461B": "雷柏 VT9Air",
   "0x461C": "雷柏 VT7Air MAX",
   "0x461D": "雷柏 VT3Air MAX",
   "0x461E": "雷柏 VT9Air MAX",
   "0x4620": "雷柏 VT0 MAX",
   "0x4621": "雷柏 VT3S MAX",
   "0x4622": "雷柏 VT2",
   "0x4623": "雷柏 VT2 MAX",
   "0x4644": "雷柏 VT1 MAX",
   "0x4647": "雷柏 VT0Air MAX MASTER",
   "0x4648": "雷柏 VT3SAir MAX MASTER",
   "0x464A": "雷柏 VT0 MAX MASTER",
   "0x464B": "雷柏 VT3S MAX MASTER",
   "0x4651": "雷柏 VT6",
   "0x4652": "雷柏 VT6MAX",
   "0x4653": "雷柏 VT9S V2",
   "0x4659": "雷柏 VT7S V2",
   "0x465B": "雷柏 VT4 V2",
   "0x465D": "雷柏 VT2S V2",
   "0x4660": "雷柏 VT7 V2",
   "0x4662": "雷柏 VT3 V2",
   "0x4664": "雷柏 VT3S V2",
   "0x4666": "雷柏 VT9 V2",
   "0x4668": "雷柏 VT2 V2",
   "0x466D": "雷柏 VT3 MAX MASTER V2",
   "0x466E": "雷柏 VT3S MAX MASTER V2",
   "0x4670": "雷柏 VT7 MAX MASTER V2",
   "0x4671": "雷柏 VT7S MAX MASTER V2",
   "0x4673": "雷柏 VT9 MAX MASTER V2",
   "0x4674": "雷柏 VT9S MAX MASTER V2",
   "0x4676": "雷柏 VT2S MAX MASTER V2",
   "0x46A1": "雷柏 VT0Air MAX",
   "0x46A2": "雷柏 VT3SAir MAX",
   "0x46A4": "雷柏 VT2K MAX",
   "0x46A5": "雷柏 VT3PRO V2",
   "0x46A6": "雷柏 VT3S PRO V2",
   "0x46A7": "雷柏 VT7PRO V2",
   "0x46A8": "雷柏 VT7S PRO V2",
   "0x46A9": "雷柏 VT9PRO V2",
   "0x46AA": "雷柏 VT9S PRO V2",
   "0x46AB": "雷柏 VT7K Air MAX V2",
   "0x46AC": "雷柏 VT2K Air MAX V2",
   "0x4B0C": "雷柏 VT3Air MAX V2",
   "0x4B0D": "雷柏 VT3SAir MAX V2",
   "0x4B0E": "雷柏 VT7Air MAX V2",
   "0x4B0F": "雷柏 VT7SAir MAX V2",
   "0x4B10": "雷柏 VT9Air MAX V2",
   "0x4B11": "雷柏 VT9SAir MAX V2"
  },
  "receivers": {
   "0x1410": "0x4610",
   "0x1411": "0x4611",
   "0x1412": "0x4612",
   "0x1413": "0x4613",
   "0x1414": "0x4614",
   "0x1415": "0x4615",
   "0x1416": "0x4616",
   "0x1417": "0x4617",
   "0x1418": "0x4618",
   "0x1419": "0x4619",
   "0x141A": "0x461A",
   "0x141B": "0x461B",
   "0x141C": "0x461C",
   "0x141D": "0x461D",
   "0x141E": "0x461E",
   "0x1420": "0x4620",
   "0x1421": "0x4621",
   "0x1422": "0x4622",
   "0x1423": "0x4623",
   "0x1444": "0x4644",
   "0x1447": "0x4647",
   "0x144A": "0x464A",
   "0x144B": "0x464B",
   "0x1451": "0x4651",
   "0x1452": "0x4652",
   "0x1453": "0x4653",
   "0x1459": "0x4659",
   "0x145B": "0x465B",
   "0x145D": "0x465D",
   "0x1460": "0x4660",
   "0x1462": "0x4662",
   "0x1464": "0x4664",
   "0x1466": "0x4666",
   "0x1468": "0x4668",
   "0x146D": "0x466D",
   "0x146E": "0x466E",
   "0x1470": "0x4670",
   "0x1471": "0x4671",
   "0x1473": "0x4673",
   "0x1474": "0x4674",
   "0x1476": "0x4676",
   "0x14A1": "0x46A1",
   "0x14A2": "0x46A2",
   "0x14A4": "0x46A4",
   "0x14A5": "0x46A5",
   "0x14A6": "0x46A6",
   "0x14A7": "0x46A7",
   "0x14A8": "0x46A8",
   "0x14A9": "0x46A9",
   "0x14AA": "0x46AA",
   "0x14AB": "0x46AB",
   "0x14AC": "0x46AC",
   "0x1B0C": "0x4B0C",
   "0x1B0D": "0x4B0D",
   "0x1B0E": "0x4B0E",
   "0x1B0F": "0x4B0F",
   "0x1B10": "0x4B10",
   "0x1B11": "0x4B11"
  }
 },
 "rapoo_inputless": [
  {
   "vid": "0x24AE",
   "pid": "0x1470",
   "name": "VT7 MAX MASTER V2"
  },
  {
   "vid": "0x24AE",
   "pid": "0x1412",
   "name": "VT3 二代"
  },
  {
   "vid": "0x24AE",
   "pid": "0x14A2",
   "name": "VT3SAir MAX"
  },
  {
   "vid": "0x24AE",
   "pid": "0x14A7",
   "name": "VT7 PRO V2"
  }
 ],
 "mchose_v2": {
  "receivers": [
   "0x1014",
   "0x1018",
   "0x101C"
  ],
  "models": {
   "0x4026": "MCHOSE A5 V3 Ultra+",
   "0x4028": "MCHOSE K7 V2 Ultra+",
   "0x4033": "MCHOSE A7 V3 Ultra+",
   "0x4034": "MCHOSE A5 V3 Ultra+ (3955)",
   "0x4037": "MCHOSE K5 Pro",
   "0x4038": "MCHOSE K5 Ultra",
   "0x4040": "MCHOSE K5 V2"
  },
  "ambiguous_reply_models": [
   "0x4026"
  ]
 },
 "lamzu_feature83_interfaces": [
  {
   "page": "0xFFA0",
   "usage": 1,
   "interface": 1
  },
  {
   "page": "0xFFFF",
   "usage": 1,
   "interface": 1
  },
  {
   "page": "0xFFFF",
   "usage": 0,
   "interface": 2
  }
 ],
 "lamzu_feature83": {
  "0x0015": {
   "name": "LAMZU MAYA 8K 接收器",
   "vid": "0x37B0",
   "target": 2
  },
  "0x001C": {
   "name": "LAMZU MAYA X",
   "target": 2
  },
  "0x001D": {
   "name": "LAMZU MAYA X 1K 接收器",
   "target": 2
  },
  "0x001E": {
   "name": "LAMZU MAYA X 8K 接收器",
   "target": 2
  },
  "0x0048": {
   "name": "LAMZU LM20 8K 接收器",
   "vid": "0x37B0",
   "target": 2
  }
 },
 "lunafury": {
  "0x0032": {
   "name": "LunaFury LUNA33",
   "target": 0
  },
  "0x0033": {
   "name": "LunaFury LUNA33",
   "target": 2
  },
  "0x0054": {
   "name": "LunaFury TYPE33",
   "target": 0
  },
  "0x0084": {
   "name": "LunaFury TYPE33",
   "target": 2
  }
 },
 "razer": {
  "0x00A4": 31,
  "0x0062": 31,
  "0x006F": 31,
  "0x0072": 63,
  "0x0077": 31,
  "0x007A": 255,
  "0x007B": 255,
  "0x007D": 63,
  "0x0083": 255,
  "0x0088": 31,
  "0x008F": 31,
  "0x0090": 31,
  "0x0094": 31,
  "0x009A": 31,
  "0x009C": 31,
  "0x009E": 31,
  "0x009F": 31,
  "0x00A5": 31,
  "0x00A6": 31,
  "0x00A7": 31,
  "0x00A8": 31,
  "0x00AA": 31,
  "0x00AB": 31,
  "0x00AF": 31,
  "0x00B0": 31,
  "0x00B3": 31,
  "0x00B4": 31,
  "0x00B6": 31,
  "0x00B7": 31,
  "0x00B8": 31,
  "0x00B9": 31,
  "0x00BE": 31,
  "0x00BF": 31,
  "0x00C0": 31,
  "0x00C1": 31,
  "0x00C2": 31,
  "0x00C3": 31,
  "0x00C5": 31,
  "0x00C8": 31,
  "0x00CC": 31,
  "0x00CD": 31,
  "0x00D1": 31,
  "0x00D4": 31,
  "0x00D7": 31,
  "0x00DF": 31,
  "0x00E5": 31,
  "0x00E6": 31,
  "0x0070": 31,
  "0x0073": 63,
  "0x007C": 63,
  "0x0080": 31,
  "0x0086": 31,
  "0x00C4": 31,
  "0x00C7": 31,
  "0x00D0": 31,
  "0x00D3": 31,
  "0x00D6": 31,
  "0x00DA": 31,
  "0x00DB": 31,
  "0x00DE": 31
 },
 "logitech": [
  "0xC0A8",
  "0xC513",
  "0xC517",
  "0xC51B",
  "0xC52B",
  "0xC52F",
  "0xC531",
  "0xC532",
  "0xC534",
  "0xC537",
  "0xC539",
  "0xC53A",
  "0xC53D",
  "0xC53F",
  "0xC541",
  "0xC543",
  "0xC545",
  "0xC547",
  "0xC548",
  "0xC54D",
  "0xC70A",
  "0xC70E",
  "0xC713",
  "0xC714",
  "0xC71B",
  "0xC71C",
  "0xC71E",
  "0xC71F"
 ],
 "lamzu": [
  {
   "vid": "0x37B0",
   "pid": "0x0015"
  },
  {
   "vid": "0x373E",
   "pid": "0x001C"
  },
  {
   "vid": "0x373E",
   "pid": "0x001D"
  },
  {
   "vid": "0x373E",
   "pid": "0x001E"
  },
  {
   "vid": "0x37B0",
   "pid": "0x0009"
  },
  {
   "vid": "0x37B0",
   "pid": "0x000F"
  },
  {
   "vid": "0x37B0",
   "pid": "0x0010"
  },
  {
   "vid": "0x37B0",
   "pid": "0x0023"
  },
  {
   "vid": "0x37B0",
   "pid": "0x002E"
  },
  {
   "vid": "0x37B0",
   "pid": "0x0032"
  },
  {
   "vid": "0x37B0",
   "pid": "0x0048"
  }
 ],
 "pulsar": [
  {
   "vid": "0x25A7",
   "pid": "0xFA7B"
  },
  {
   "vid": "0x25A7",
   "pid": "0xFA7C"
  },
  {
   "vid": "0x3554",
   "pid": "0xF507"
  },
  {
   "vid": "0x3554",
   "pid": "0xF508"
  },
  {
   "vid": "0x3710",
   "pid": "0x3414"
  },
  {
   "vid": "0x3710",
   "pid": "0x5406"
  }
 ],
 "atk_phantom": [
  "0x12CE",
  "0x1312"
 ],
 "vaxee_model_names": {
  "0x1003": "VAXEE XE Wireless",
  "0x1004": "ZYGEN NP-01S Wireless",
  "0x1005": "VAXEE AX Wireless",
  "0x1006": "ZYGEN NP-01 Wireless",
  "0x1007": "VAXEE XE-S Wireless",
  "0x1008": "VAXEE XE-S-L Wireless",
  "0x1009": "VAXEE x NINJUTSO Sora Wireless",
  "0x1010": "VAXEE E1 Wireless",
  "0x1011": "ZYGEN NP-01S V2 Wireless",
  "0x1012": "VAXEE XE V2 Wireless",
  "0x1013": "ZYGEN NP-01S Ergo Wireless",
  "0x1014": "VAXEE NP-01S V3 Wireless",
  "0x1015": "VAXEE NP-01 Ergo Wireless"
 },
 "ko_one_feature83": {
  "0x006A": {
   "name": "CRDRAKO KO-ONE",
   "target": 0
  },
  "0x006B": {
   "name": "CRDRAKO KO-ONE",
   "target": 2
  }
 },
 "rog_standard": {
  "0x1A72": {
   "name": "ROG Gladius III Aimpoint",
   "wired": false,
   "verified": false,
   "evidence": "SOURCES.md: 2026-10-08 DB47, G-Helper GladiusIIIAimpoint.cs"
  },
  "0x1A70": {
   "name": "ROG Gladius III Aimpoint",
   "wired": true,
   "verified": false,
   "evidence": "SOURCES.md: 2026-10-08 DB47, G-Helper GladiusIIIAimpoint.cs"
  },
  "0x1B0C": {
   "name": "ROG Gladius III Eva 2",
   "wired": fa
```

## 四、上游库：型号 ↔ VID:PID ↔ 协议族（318 条）

| 来源 | 型号 | VID:PID | 协议驱动 | 电量 |
|---|---|---|---|---|
| libratbag | ASUS ROG GX860 Buzzard Mouse | 0b05:1816 | asus | unknown |
| libratbag | ASUS ROG Chakram Core | 0b05:1958 | asus | unknown |
| libratbag | ASUS ROG Chakram X | 0b05:1a18 | asus | unknown |
| libratbag | ASUS ROG Chakram X | 0b05:1a1a | asus | unknown |
| libratbag | ASUS ROG Chakram | 0b05:18e3 | asus | unknown |
| libratbag | ASUS ROG Chakram | 0b05:18e5 | asus | unknown |
| libratbag | ASUS ROG Gladius II Origin PNK LTD | 0b05:18cd | asus | unknown |
| libratbag | ASUS ROG Gladius II Origin | 0b05:1877 | asus | unknown |
| libratbag | ASUS ROG Gladius II | 0b05:1845 | asus | unknown |
| libratbag | ASUS ROG Harpe Wireless | 0b05:1a92 | asus | unknown |
| libratbag | ASUS ROG Keris Wireless AimPoint | 0b05:1a66 | asus | unknown |
| libratbag | ASUS ROG Keris Wireless AimPoint | 0b05:1a68 | asus | unknown |
| libratbag | ASUS ROG Keris Wireless | 0b05:195e | asus | unknown |
| libratbag | ASUS ROG Keris Wireless | 0b05:1960 | asus | unknown |
| libratbag | ASUS ROG Keris | 0b05:195c | asus | unknown |
| libratbag | ASUS ROG Omni Receiver | 0b05:1ace | asus | unknown |
| libratbag | ASUS ROG Pugio | 0b05:1846 | asus | unknown |
| libratbag | ASUS ROG Spatha X | 0b05:1979 | asus | unknown |
| libratbag | ASUS ROG Spatha X | 0b05:1977 | asus | unknown |
| libratbag | ASUS ROG Strix Carry | 0b05:18b4 | asus | unknown |
| libratbag | ASUS ROG Strix Impact | 0b05:1847 | asus | unknown |
| libratbag | ASUS ROG Strix Impact II Wireless | 0b05:1947 | asus | unknown |
| libratbag | ASUS ROG Strix Impact II Wireless | 0b05:1949 | asus | unknown |
| libratbag | ASUS ROG Strix Impact II | 0b05:18e1 | asus | unknown |
| libratbag | ASUS ROG Strix Impact III Wireless | 0b05:1ad7 | asus | unknown |
| libratbag | ASUS ROG Strix Impact III | 0b05:1a88 | asus | unknown |
| libratbag | ASUSTeK TUF GAMING M3 GEN II | 0b05:1a9b | asus | unknown |
| libratbag | ASUS TUF GAMING M4 AIR | 0b05:1a03 | asus | unknown |
| libratbag | ASUS TUF GAMING MINI WL MOUSE MIKU | 0b05:1c56 | asus | unknown |
| libratbag | ASUS TUF GAMING MINI WL MOUSE MIKU | 0b05:1c57 | asus | unknown |
| libratbag | Etekcity Scroll Alpha | 1ea7:4011 | etekcity | unknown |
| libratbag | Glorious Model D | 258a:0033 | sinowealth | unknown |
| libratbag | Glorious Model O Eternal | 3794:a000 | sinowealth | unknown |
| libratbag | Glorious Model O/O- | 258a:0036 | sinowealth | unknown |
| libratbag | G.Skill MX-780 | 28da:3101 | gskill | unknown |
| libratbag | Logitech M325 | 046d:400a | hidpp20 | unknown |
| libratbag | Logitech M500s | 046d:c093 | hidpp20 | unknown |
| libratbag | Logitech M545 | 046d:4052 | hidpp20 | unknown |
| libratbag | Logitech M570 | 046d:1028 | hidpp10 | unknown |
| libratbag | Logitech M585/M590 | 046d:406b | hidpp20 | unknown |
| libratbag | Logitech M705 | 046d:101b | hidpp10 | unknown |
| libratbag | Logitech M720 | 046d:405e | hidpp20 | unknown |
| libratbag | Logitech MX Anywhere 3 | 046d:4090 | hidpp20 | unknown |
| libratbag | Logitech MX Anywhere 2 | 046d:404a | hidpp20 | unknown |
| libratbag | Logitech MX Anywhere 2 | 046d:4072 | hidpp20 | unknown |
| libratbag | Logitech MX Anywhere 2 | 046d:4063 | hidpp20 | unknown |
| libratbag | Logitech MX Anywhere 2S | 046d:406a | hidpp20 | unknown |
| libratbag | Logitech MX Ergo | 046d:406f | hidpp20 | unknown |
| libratbag | Logitech MX Master 2S | 046d:4069 | hidpp20 | unknown |
| libratbag | Logitech MX Master 3 | 046d:4082 | hidpp20 | unknown |
| libratbag | Logitech MX Master | 046d:4041 | hidpp20 | unknown |
| libratbag | Logitech MX Master | 046d:4060 | hidpp20 | unknown |
| libratbag | Logitech MX Master | 046d:4071 | hidpp20 | unknown |
| libratbag | Logitech MX Vertical | 046d:407b | hidpp20 | unknown |
| libratbag | Logitech MX Vertical | 046d:c08a | hidpp20 | unknown |
| libratbag | Logitech MX Vertical | 046d:c077 | hidpp20 | unknown |
| libratbag | MX518 | 046d:c08e | hidpp20 | unknown |
| libratbag | Logitech Marathon M705 | 046d:406d | hidpp20 | unknown |
| libratbag | Logitech T650 | 046d:4101 | hidpp20 | unknown |
| libratbag | Logitech Wireless Touchpad | 046d:4011 | hidpp20 | unknown |
| libratbag | Logitech G Powerplay | 046d:405f | hidpp20 | unknown |
| libratbag | Logitech G PRO Keyboard | 046d:c339 | hidpp20 | unknown |
| libratbag | Logitech G Pro Wireless | 046d:4079 | hidpp20 | unknown |
| libratbag | Logitech G Pro Wireless | 046d:c088 | hidpp20 | unknown |
| libratbag | Logitech G Pro X Wireless Superlight | 046d:4093 | hidpp20 | unknown |
| libratbag | Logitech G Pro X Wireless Superlight | 046d:c094 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G Pro | 046d:c085 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G Pro | 046d:c08c | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G102/G103/G203 | 046d:c084 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G102/G103/G203 | 046d:c092 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G102/G103/G203 | 046d:c09d | hidpp20 | unknown |
| libratbag | Logitech G300 | 046d:c246 | logitech_g300 | unknown |
| libratbag | Logitech Gaming Mouse G302 | 046d:c07f | hidpp20 | unknown |
| libratbag | Logitech G303 Shroud Edition | 046d:c097 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G303 | 046d:c080 | hidpp20 | unknown |
| libratbag | Logitech Gaming Mouse G305 | 046d:4074 | hidpp20 | unknown |
| libratbag | Logitech G402 Gaming Mouse | 046d:c07e | hidpp20 | unknown |
| libratbag | Logitech G403 Hero | 046d:c08f | hidpp20 | unknown |
| libratbag | Logitech G403 Wireless | 046d:405d | hidpp20 | unknown |
| libratbag | Logitech G403 Wireless | 046d:c082 | hidpp20 | unknown |
| libratbag | Logitech G403 | 046d:c083 | hidpp20 | unknown |
| libratbag | Logitech G413 | 046d:c33a | hidpp20 | unknown |
| libratbag | Logitech G5 | 046d:c049 | hidpp10 | unknown |
| libratbag | G5 | 046d:c041 | hidpp10 | unknown |
| libratbag | Logitech G500 | 046d:c068 | hidpp10 | unknown |
| libratbag | Logitech G500s | 046d:c24e | hidpp10 | unknown |
| libratbag | Logitech G502 Hero Wireless | 046d:407f | hidpp20 | unknown |
| libratbag | Logitech G502 Hero Wireless | 046d:c08d | hidpp20 | unknown |
| libratbag | Logitech G502 Hero | 046d:c08b | hidpp20 | unknown |
| libratbag | Logitech G502 Proteus Core | 046d:c07d | hidpp20 | unknown |
| libratbag | Logitech G502 Proteus Spectrum | 046d:c332 | hidpp20 | unknown |
| libratbag | Logitech G502 X PLUS | 046d:4099 | hidpp20 | unknown |
| libratbag | Logitech G502 X PLUS | 046d:c095 | hidpp20 | unknown |
| libratbag | Logitech G502 X Wireless | 046d:c098 | hidpp20 | unknown |
| libratbag | Logitech G502 X Wireless | 046d:409f | hidpp20 | unknown |
| libratbag | Logitech G502 X | 046d:c099 | hidpp20 | unknown |
| libratbag | Logitech G513 | 046d:c33c | hidpp20 | unknown |
| libratbag | Logitech G600 | 046d:c24a | logitech_g600 | unknown |
| libratbag | Logitech G602 | 046d:402c | hidpp20 | unknown |
| libratbag | Logitech G603 | 046d:406c | hidpp20 | unknown |
| libratbag | Logitech G604 | 046d:4085 | hidpp20 | unknown |
| libratbag | Logitech G635 | 046d:0a89 | hidpp20 | unknown |
| libratbag | Logitech G7 | 046d:c51a | hidpp10 | unknown |
| libratbag | Logitech G700 | 046d:c531 | hidpp10 | unknown |
| libratbag | Logitech G700 | 046d:c06b | hidpp10 | unknown |
| libratbag | Logitech G700s | 046d:c07c | hidpp10 | unknown |
| libratbag | Logitech G703 Hero | 046d:4086 | hidpp20 | unknown |
| libratbag | Logitech G703 Hero | 046d:c090 | hidpp20 | unknown |
| libratbag | Logitech G703 | 046d:c087 | hidpp20 | unknown |
| libratbag | Logitech G703 | 046d:4070 | hidpp20 | unknown |
| libratbag | Logitech G705 | 046d:c096 | hidpp20 | unknown |
| libratbag | Logitech G705 | 046d:409d | hidpp20 | unknown |
| libratbag | Logitech G733 Gaming Headset | 046d:0ab5 | hidpp20 | unknown |
| libratbag | Logitech G733 Gaming Headset | 046d:0afe | hidpp20 | unknown |
| libratbag | Logitech G815 | 046d:c33f | hidpp20 | unknown |
| libratbag | Logitech G9 | 046d:c048 | hidpp10 | unknown |
| libratbag | Logitech G900 | 046d:c081 | hidpp20 | unknown |
| libratbag | Logitech G900 | 046d:4053 | hidpp20 | unknown |
| libratbag | Logitech G903 Hero | 046d:c091 | hidpp20 | unknown |
| libratbag | Logitech G903 Hero | 046d:4087 | hidpp20 | unknown |
| libratbag | Logitech G903 | 046d:c086 | hidpp20 | unknown |
| libratbag | Logitech G903 | 046d:4067 | hidpp20 | unknown |
| libratbag | Logitech G910 | 046d:c335 | hidpp20 | unknown |
| libratbag | Logitech G915 TKL | 046d:c343 | hidpp20 | unknown |
| libratbag | Logitech G915 | 046d:c33e | hidpp20 | unknown |
| libratbag | Logitech G935 | 046d:0a87 | hidpp20 | unknown |
| libratbag | Logitech G9x [Call of Duty MW3 Edition] | 046d:c249 | hidpp10 | unknown |
| libratbag | G9x [Original] | 046d:c066 | hidpp10 | unknown |
| libratbag | Mars Gaming MM4 | 04d9:fa58 | marsgaming | unknown |
| libratbag | Nubwo x7 spectrum | 258a:0012 | sinowealth_nubwo | unknown |
| libratbag | openinput | 1d50:616a | openinput | unknown |
| libratbag | Roccat Kone EMP | 1e7d:2e24 | roccat-kone-emp | unknown |
| libratbag | Roccat Kone Pure | 1e7d:2dc2 | roccat-kone-pure | unknown |
| libratbag | Roccat Kone Pure | 1e7d:2dbe | roccat-kone-pure | unknown |
| libratbag | Roccat Kone Pure | 1e7d:2dcb | roccat-kone-pure | unknown |
| libratbag | Roccat Kone XTD | 1e7d:2e22 | roccat | unknown |
| libratbag | SinoWealth Generic Mouse (0027) | 258a:0027 | sinowealth | unknown |
| libratbag | SinoWealth Generic Mouse (0029) | 258a:0029 | sinowealth | unknown |
| libratbag | Sinowealth Generic Mouse (0051) | 258a:0051 | sinowealth | unknown |
| libratbag | SinoWealth Generic Mouse (1007) | 258a:1007 | sinowealth | unknown |
| libratbag | SteelSeries Kinzu V2 Pro Edition | 1038:1366 | steelseries | unknown |
| libratbag | SteelSeries Kinzu V2 | 1038:1378 | steelseries | unknown |
| libratbag | SteelSeries Kinzu V3 | 1038:1388 | steelseries | unknown |
| libratbag | SteelSeries Rival 100/105 | 1038:1702 | steelseries | unknown |
| libratbag | SteelSeries Rival 100/105 | 1038:170a | steelseries | unknown |
| libratbag | SteelSeries Rival 100/105 | 1038:170b | steelseries | unknown |
| libratbag | SteelSeries Rival 100/105 | 1038:170c | steelseries | unknown |
| libratbag | SteelSeries Rival 100/105 | 1038:1814 | steelseries | unknown |
| libratbag | SteelSeries Rival 310 | 1038:1720 | steelseries | unknown |
| libratbag | SteelSeries Rival 600 | 1038:1724 | steelseries | unknown |
| libratbag | SteelSeries Rival 650 Wireless | 1038:172b | steelseries | unknown |
| libratbag | SteelSeries Rival 650 Wireless | 1038:1726 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1384 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1392 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1710 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1712 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:171c | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1394 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:171a | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1716 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1714 | steelseries | unknown |
| libratbag | SteelSeries Rival | 1038:1718 | steelseries | unknown |
| libratbag | SteelSeries Sensei 310 | 1038:1722 | steelseries | unknown |
| libratbag | SteelSeries Sensei Raw | 1038:1369 | steelseries | unknown |
| openrazer | Razer Viper Mini | 1532:008a | razer | no |
| openrazer | Razer Lancehead Wireless (Wired) | 1532:0070 | razer | yes |
| openrazer | Razer Lancehead Wireless (Receiver) | 1532:006f | razer | no |
| openrazer | Razer Lancehead (Wired) | 1532:0059 | razer | yes |
| openrazer | Razer Lancehead (Wireless) | 1532:005a | razer | no |
| openrazer | Razer DeathAdder Essential (White Edition) | 1532:0071 | razer | no |
| openrazer | Razer Abyssus Elite (D.Va Edition) | 1532:006a | razer | no |
| openrazer | Razer Abyssus Essential | 1532:006b | razer | no |
| openrazer | Razer Lancehead Tournament Edition | 1532:0060 | razer | no |
| openrazer | Razer Mamba Chroma (Wireless) | 1532:0045 | razer | yes |
| openrazer | Razer Mamba Chroma (Wired) | 1532:0044 | razer | yes |
| openrazer | Razer Mamba Tournament Edition | 1532:0046 | razer | no |
| openrazer | Razer Abyssus | 1532:0042 | razer | no |
| openrazer | Razer Imperator 2012 | 1532:002f | razer | no |
| openrazer | Razer Ouroboros | 1532:0032 | razer | yes |
| openrazer | Razer Orochi 2013 | 1532:0039 | razer | no |
| openrazer | Razer Orochi (Wired) | 1532:0048 | razer | no |
| openrazer | Razer DeathAdder Chroma | 1532:0043 | razer | no |
| openrazer | Razer DeathAdder 2000 | 1532:004f | razer | no |
| openrazer | Razer DeathAdder 2013 | 1532:0037 | razer | no |
| openrazer | Razer Naga Hex V2 | 1532:0050 | razer | no |
| openrazer | Razer Naga | 1532:0015 | razer | no |
| openrazer | Razer Naga 2012 | 1532:002e | razer | no |
| openrazer | Razer Naga Chroma | 1532:0053 | razer | no |
| openrazer | Razer Naga Trinity | 1532:0067 | razer | no |
| openrazer | Razer Naga Hex | 1532:0041 | razer | no |
| openrazer | Razer Naga Hex (Red) | 1532:0036 | razer | no |
| openrazer | Razer Taipan | 1532:0034 | razer | no |
| openrazer | Razer DeathAdder Elite | 1532:005c | razer | no |
| openrazer | Razer Diamondback Chroma | 1532:004c | razer | no |
| openrazer | Razer DeathAdder 3.5G | 1532:0016 | razer | no |
| openrazer | Razer DeathAdder 3.5G Black | 1532:0029 | razer | no |
| openrazer | Razer Mamba 2012 (Wireless) | 1532:0025 | razer | yes |
| openrazer | Razer Mamba 2012 (Wired) | 1532:0024 | razer | yes |
| openrazer | Razer Mamba Wireless (Wired) | 1532:0073 | razer | yes |
| openrazer | Razer Mamba Wireless (Receiver) | 1532:0072 | razer | no |
| openrazer | Razer Naga 2014 | 1532:0040 | razer | no |
| openrazer | Razer Orochi 2011 | 1532:0013 | razer | no |
| openrazer | Razer Abyssus V2 | 1532:005b | razer | no |
| openrazer | Razer Abyssus 1800 | 1532:0020 | razer | no |
| openrazer | Razer Abyssus 2000 | 1532:005e | razer | no |
| openrazer | Razer DeathAdder 3500 | 1532:0054 | razer | no |
| openrazer | Razer Viper Ultimate (Wired) | 1532:007a | razer | yes |
| openrazer | Razer Viper Ultimate (Wireless) | 1532:007b | razer | no |
| openrazer | Razer Viper | 1532:0078 | razer | no |
| openrazer | Razer DeathAdder Essential | 1532:006e | razer | no |
| openrazer | Razer DeathAdder Essential (2021) | 1532:0098 | razer | no |
| openrazer | Razer Mamba Elite | 1532:006c | razer | no |
| openrazer | Razer Naga Left Handed Edition 2020 | 1532:008d | razer | no |
| openrazer | Razer Naga Pro (Wired) | 1532:008f | razer | yes |
| openrazer | Razer Naga Pro (Wireless) | 1532:0090 | razer | no |
| openrazer | Razer Naga V2 Pro (Wired) | 1532:00a7 | razer | yes |
| openrazer | Razer Naga V2 Pro (Wireless) | 1532:00a8 | razer | no |
| openrazer | Razer DeathAdder 1800 | 1532:0038 | razer | no |
| openrazer | Razer Basilisk | 1532:0064 | razer | no |
| openrazer | Razer Basilisk Essential | 1532:0065 | razer | no |
| openrazer | Razer Basilisk Ultimate | 1532:0086 | razer | yes |
| openrazer | RazerBasiliskUltimateReceiver | 1532:0088 | razer | no |
| openrazer | Razer Basilisk V2 | 1532:0085 | razer | no |
| openrazer | Razer Basilisk V3 | 1532:0099 | razer | no |
| openrazer | Razer DeathAdder V2 | 1532:0084 | razer | no |
| openrazer | Razer DeathAdder V2 Pro (Wired) | 1532:007c | razer | yes |
| openrazer | Razer DeathAdder V2 Pro (Wireless) | 1532:007d | razer | no |
| openrazer | Razer Atheris (Receiver) | 1532:0062 | razer | yes |
| openrazer | Razer Basilisk X HyperSpeed | 1532:0083 | razer | yes |
| openrazer | Razer Orochi V2 (Receiver) | 1532:0094 | razer | yes |
| openrazer | Razer Orochi V2 (Bluetooth) | 1532:0095 | razer | no |
| openrazer | Razer Naga X | 1532:0096 | razer | no |
| openrazer | Razer DeathAdder V2 Mini | 1532:008c | razer | no |
| openrazer | Razer Viper 8KHz | 1532:0091 | razer | no |
| openrazer | Razer Viper Mini SE (Wired) | 1532:009e | razer | yes |
| openrazer | Razer Viper Mini SE (Wireless) | 1532:009f | razer | no |
| openrazer | Razer Naga Epic Chroma (Wired) | 1532:003e | razer | yes |
| openrazer | Razer Naga Epic Chroma (Wireless) | 1532:003f | razer | no |
| openrazer | Razer Pro Click (Receiver) | 1532:0077 | razer | yes |
| openrazer | Razer Pro Click (Wired) | 1532:0080 | razer | no |
| openrazer | Razer DeathAdder V2 X HyperSpeed | 1532:009c | razer | yes |
| openrazer | Razer Naga Epic | 1532:001f | razer | yes |
| openrazer | Razer Viper V2 Pro (Wired) | 1532:00a5 | razer | yes |
| openrazer | Razer Viper V2 Pro (Wireless) | 1532:00a6 | razer | no |
| openrazer | Razer Cobra Pro (Wired) | 1532:00af | razer | yes |
| openrazer | Razer Cobra Pro (Wireless) | 1532:00b0 | razer | no |
| openrazer | Razer Cobra HyperSpeed | 1532:00da | razer | yes |
| openrazer | Razer Cobra HyperSpeed Receiver | 1532:00db | razer | no |
| openrazer | Razer DeathAdder V3 | 1532:00b2 | razer | no |
| openrazer | Razer DeathAdder V3 Pro (Wired) | 1532:00b6 | razer | yes |
| openrazer | Razer DeathAdder V3 Pro (Wireless) | 1532:00b7 | razer | no |
| openrazer | Razer DeathAdder V3 Pro (Wired) | 1532:00c2 | razer | no |
| openrazer | Razer DeathAdder V3 Pro (Wireless) | 1532:00c3 | razer | no |
| openrazer | Razer Basilisk V3 Pro (Wired) | 1532:00aa | razer | yes |
| openrazer | Razer Basilisk V3 Pro (Wireless) | 1532:00ab | razer | no |
| openrazer | Razer Basilisk V3 Pro 35K (Wired) | 1532:00cc | razer | yes |
| openrazer | Razer Basilisk V3 Pro 35K (Wireless) | 1532:00cd | razer | no |
| openrazer | Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | 1532:00d6 | razer | yes |
| openrazer | Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | 1532:00d7 | razer | no |
| openrazer | Razer Basilisk V3 35K | 1532:00cb | razer | no |
| openrazer | Razer HyperPolling Wireless Dongle | 1532:00b3 | razer | yes |
| openrazer | Razer Pro Click Mini (Receiver) | 1532:009a | razer | yes |
| openrazer | Razer DeathAdder V2 Lite | 1532:00a1 | razer | no |
| openrazer | Razer Cobra | 1532:00a3 | razer | no |
| openrazer | Razer Naga V2 HyperSpeed (Receiver) | 1532:00b4 | razer | yes |
| openrazer | Razer Viper V3 HyperSpeed | 1532:00b8 | razer | yes |
| openrazer | Razer Basilisk V3 X HyperSpeed | 1532:00b9 | razer | yes |
| openrazer | Razer Basilisk Mobile (Wired) | 1532:00d3 | razer | yes |
| openrazer | Razer Basilisk Mobile (Receiver) | 1532:00d4 | razer | no |
| openrazer | Razer DeathAdder V4 Pro (Wired) | 1532:00be | razer | yes |
| openrazer | Razer DeathAdder V4 Pro (Wireless) | 1532:00bf | razer | no |
| openrazer | Razer Viper V3 Pro (Wired) | 1532:00c0 | razer | yes |
| openrazer | Razer Viper V3 Pro (Wireless) | 1532:00c1 | razer | no |
| openrazer | Razer Viper V3 Pro SE (Wired) | 1532:00de | razer | yes |
| openrazer | Razer Viper V3 Pro SE (Wireless) | 1532:00df | razer | no |
| openrazer | Razer DeathAdder V3 HyperSpeed (Wired) | 1532:00c4 | razer | yes |
| openrazer | Razer DeathAdder V3 HyperSpeed (Wireless) | 1532:00c5 | razer | no |
| openrazer | Razer Pro Click V2 Vertical Edition (Wired) | 1532:00c7 | razer | yes |
| openrazer | Razer Pro Click V2 Vertical Edition (Wireless) | 1532:00c8 | razer | no |
| openrazer | Razer Pro Click V2 (Wired) | 1532:00d0 | razer | yes |
| openrazer | Razer Pro Click V2 (Wireless) | 1532:00d1 | razer | no |
| solaar | G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard | 046d:c343 | hidpp4.2 | unknown |
| solaar | Illuminated Keyboard | 046d:c318 | hidpp1.0 | unknown |
| solaar | G213 Prodigy Gaming Keyboard | 046d:c336 | hidpp | unknown |
| solaar | G512 RGB Mechanical Gaming Keyboard | 046d:c33c | hidpp | unknown |
| solaar | G815 Mechanical Keyboard | 046d:c33f | hidpp | unknown |
| solaar | K845 Mechanical Keyboard | 046d:c341 | hidpp | unknown |
| solaar | G700 Gaming Mouse | 046d:c06b | hidpp1.0 | yes |
| solaar | G700s Gaming Mouse | 046d:c07c | hidpp1.0 | yes |
| solaar | MX Vertical Wireless Mouse | 046d:c08a | hidpp4.5 | yes |
| solaar | PRO X Wireless | 046d:c094 | hidpp | unknown |
| solaar | G9 Laser Mouse | 046d:c048 | hidpp1.0 | yes |
| solaar | G9x Laser Mouse | 046d:c066 | hidpp1.0 | yes |
| solaar | G502 Gaming Mouse | 046d:c07d | hidpp | yes |
| solaar | G402 Gaming Mouse | 046d:c07e | hidpp | yes |
| solaar | G900 Chaos Spectrum Gaming Mouse | 046d:c081 | hidpp | yes |
| solaar | G403 Gaming Mouse | 046d:c082 | hidpp | yes |
| solaar | G903 Lightspeed Gaming Mouse | 046d:c086 | hidpp | yes |
| solaar | G703 Lightspeed Gaming Mouse | 046d:c087 | hidpp | yes |
| solaar | GPro Gaming Mouse | 046d:c088 | hidpp | yes |
| solaar | G502 SE Hero Gaming Mouse | 046d:c08b | hidpp | yes |
| solaar | G502 Lightspeed Gaming Mouse | 046d:c08d | hidpp | yes |
| solaar | MX518 Gaming Mouse | 046d:c08e | hidpp | yes |
| solaar | G703 Hero Gaming Mouse | 046d:c090 | hidpp | yes |
| solaar | G903 Hero Gaming Mouse | 046d:c091 | hidpp | yes |
| solaar | (unnamed) | 046d:c092 | hidpp | unknown |
| solaar | M500S Mouse | 046d:c093 | hidpp | yes |
| solaar | (unnamed) | 046d:c24a | hidpp | yes |
| solaar | G500 Gaming Mouse | 046d:c068 | hidpp1.0 | yes |
| solaar | G500s Gaming Mouse | 046d:c24e | hidpp1.0 | yes |
| solaar | G502 Proteus Spectrum Optical Mouse | 046d:c332 | hidpp | yes |
| solaar | Logitech PRO Gaming Keyboard | 046d:c339 | hidpp | unknown |
| solaar | G533 Gaming Headset | 046d:0a66 | hidpp2.0 | unknown |
| solaar | G535 Gaming Headset | 046d:0ac4 | hidpp2.0 | unknown |
| solaar | G935 Gaming Headset | 046d:0a87 | hidpp2.0 | unknown |
| solaar | G733 Gaming Headset | 046d:0ab5 | hidpp2.0 | unknown |
| solaar | G733 Gaming Headset | 046d:0afe | hidpp2.0 | unknown |
| solaar | PRO X Wireless Gaming Headset | 046d:0aba | hidpp2.0 | unknown |

## 五、按品牌的 VID 归属

| VID | 品牌 |
|---|---|
| 0x0414 | Gigabyte |
| 0x045E | Microsoft |
| 0x046D | Logitech |
| 0x04D9 | Unknown(04d9) |
| 0x05AC | Apple |
| 0x0728 | WAIZOWL |
| 0x093A | PixArt (Sora/SC900) |
| 0x0951 | HyperX |
| 0x09DA | A4Tech/Bloody |
| 0x0B05 | ASUS |
| 0x0C45 | Sonix (ZOWIE) |
| 0x0DB0 | MSI |
| 0x0E8D | MediaTek (AM35) |
| 0x1038 | SteelSeries |
| 0x1532 | Razer |
| 0x1915 | RAWM |
| 0x1B1C | Corsair |
| 0x1D50 | OpenInput |
| 0x1E7D | ROCCAT |
| 0x1EA7 | Unknown(1ea7) |
| 0x24AE | Rapoo |
| 0x2516 | CoolerMaster |
| 0x258A | SinoWealth |
| 0x25A7 | Areson |
| 0x28DA | Unknown(28da) |
| 0x3151 | AM Infinity |
| 0x32C2 | BYKC/AULA |
| 0x33E4 | G-Wolves |
| 0x3554 | Compx (VGN/Lamzu/Ninjutso) |
| 0x361D | Finalmouse |
| 0x362D | RIDLAG |
| 0x363C | AUSDOM |
| 0x3710 | Pulsar |
| 0x373B | ATK |
| 0x3794 | Unknown(3794) |
| 0x3837 | MCHOSE |
| 0x3886 | ZYNOO |
| 0x391D | VGN |
| 0x395A | SCYROX |

## 六、还缺什么

有帧级协议的：罗技 HID++ 0x1004、雷蛇 V4 控制传输（Feature 0x5B + XOR）、赛睿 Aerox（D2 台阶）、ROG 四种布局、雷柏两条、达尔优 A980 Pro、迈从 M HUB、VGN 两条、ATK/Lamzu/G-Wolves 的 COMPX、Finalmouse ULX/SLX、Pulsar、SCYROX、WAIZOWL、RIDLAG、RAWM、AUSDOM、EXX、AM Infinity、ZYNOO、AKKO、LunaFury/AttackShark/WLMouse 的 Feature83、狼蛛/BYKC、Sora V3。

只有 VID/PID 和协议族、没有帧格式的：Corsair（1B1C）、ROCCAT（1E7D）、MSI（0DB0）、Gigabyte（0414）、CoolerMaster（2516）、HyperX（0951）、Microsoft（045E）、Apple（05AC）、ZOWIE（0C45）。这些要么走标准 HID 电量，要么得自己抓。

上游库一条都没收的：黑爵 AJ 系列、前行者、红龙 M 系列、富勒、血手幽灵、多彩、Keychron、Glorious、Xtrfy、Endgame Gear、Vaxee、Pwnage、Zaunkoenig、Kensington、3Dconnexion、Elecom。不是遗漏，是确实没人公开过。

雷柏和达尔优补上了，在第七节。雷柏这边是 VT9S（0x24AE:0x1453，battery=rapoo，实机验证过）加 VT2 MAX 和 0x14A5 两条 reader，还有 V700 那条键盘接收器。达尔优是 A980 Pro（Feature 0x0A）、FLEX87（0x260D:0x0211）、A98 共享接收器（0x260D:0x0037）。达尔优鼠标本身的 VID:PID 没找到公开记录，0x260D 是接收器侧的，别当成鼠标的用。

## 七、接收器与无线设备（185 条，鼠标 88 条）

| 类别 | 型号 | VID:PID | 协议 | 连接 | 已验证 |
|---|---|---|---|---|---|
| keyboard | 雷柏 V700DIY-98 长续航版 | 0x24AE:0x1924 | rapoo_v700_input |  | 否 |
| mouse | VGN 蜻蜓 3 SE | 0x391D:0x1A01 | vgn_dragonfly3_se | 2.4G | 否 |
| mouse | IPI 雾 | 0x372E:0x10F9 | ipi_uni |  | 否 |
| mouse | 西伯利亚 X7 系列 / 8K 接收器 | 0x36F9:0xAA20 | xiberia_x7 |  | 否 |
| mouse | 前行者 X23 Pro（老款） | 0x04F3:0x026F | x23_pro_input |  | 否 |
| mouse | 前行者 X23 星闪版 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23 Max | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23 SE | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23 Pro | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23 碳纤维版 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23L 碳纤维版 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 X23 星闪版 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23 Max | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23 SE | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23 Pro | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23 碳纤维版 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23L 碳纤维版 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 X23 Max | 0x3554:0xF5A0 | x23_compx | 有线 | 否 |
| mouse | 前行者 X23 Max | 0x3554:0xF59F | x23_compx | 2.4G | 否 |
| mouse | 前行者 X23 Max | 0x3554:0xF5B2 | x23_compx | 2.4G | 否 |
| mouse | 前行者 X23 Max | 0x3554:0xF5B3 | x23_compx | 有线 | 否 |
| mouse | 前行者 X23 Max | 0x3554:0xF5B5 | x23_compx | 2.4G | 否 |
| mouse | 前行者鼠标接收器 | 0x3554:0xFB35 | x23_compx | 2.4G | 否 |
| mouse | 前行者有线鼠标 | 0x3554:0xFB36 | x23_compx | 有线 | 否 |
| mouse | 前行者 X23 标准版 | 0x089D:0x062F | x23_standard |  | 否 |
| keyboard | 达尔优 FLEX87 | 0x260D:0x0211 | dareu_flex87 |  | 否 |
| keyboard | 达尔优共享接收器（待确认键盘） | 0x260D:0x0037 | dareu_a98 | 2.4G | 否 |
| mouse | AJAZZ 2.4G 接收器 | 0x3151:0x4027 | ajazz_159pro |  | 否 |
| mouse | AJAZZ AJ159 / AJ159 P 接收器 | 0x248A:0x5C2F | ajazz_159p |  | 否 |
| mouse | AJAZZ AJ159 / AJ159 P 接收器 | 0x249A:0x5C2F | ajazz_159p |  | 否 |
| mouse | AJAZZ AJ159 / AJ159 P 接收器 | 0x248A:0x5B2F | ajazz_159p_legacy |  | 否 |
| mouse | 多彩 M700 Ultra / Pro+ 2.4G | 0x320F:0x228A | delux_m700 |  | 否 |
| mouse | 1D57:FA65 无线鼠标 | 0x1D57:0xFA65 | delux_pro_input |  | 否 |
| mouse | 前行者 S9 SE | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 S9 SE | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A6 Pro | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 A6 Pro | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A6 Max | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 A6 Max | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A7 SE | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 A7 SE | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A7 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 A7 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A7 Pro | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 A7 Pro | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E7 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E7 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E7 Pro | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E7 Pro | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E9 Carbon | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E9 Carbon | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E9 Pro Carbon | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E9 Pro Carbon | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E9 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E9 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E11 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E11 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 E11 Pro | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 E11 Pro | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A6 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 G2SE A | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 G2SE A | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 A6 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 S7 Plus | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 S7 Plus | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 S2 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 S2 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 S7 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 S7 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 S7 SE | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 S7 SE | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者 G5 | 0xA8A4:0x2255 | x23_freq | 有线 | 否 |
| mouse | 前行者 G5 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 前行者有线鼠标 | 0x3554:0xF515 | x23_compx | 有线 | 否 |
| mouse | 前行者鼠标接收器 | 0x3554:0xF517 | x23_compx | 2.4G | 否 |
| mouse | 前行者鼠标接收器 | 0x3554:0xFB45 | x23_compx | 2.4G | 否 |
| mouse | 前行者有线鼠标 | 0x3554:0xFB47 | x23_compx | 有线 | 否 |
| mouse | 前行者鼠标接收器 | 0x3554:0xFB48 | x23_compx | 2.4G | 否 |
| mouse | 名龙星球 MX300 | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX301 | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX301 PRO | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX301 PRO Gold | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX301 PRO Lite | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX301 PRO V2 | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX302 | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | 名龙星球 MX306 | 0xA8A5:0x2255 | mloong_mx301pro | 2.4G | 否 |
| mouse | VGN 蜻蜓 3 大师版 | 0x391D:0x1A05 | vgn_dragonfly3_master |  | 否 |
| mouse | VGN 蜻蜓 F2 大师版 (MasterGT) | 0x391D:0x102D | vgn_dragonfly3_master |  | 否 |
| mouse | Pulsar Fusion2 1K 接收器 | 0x3710:0x5402 | pulsar_sonix |  | 否 |
| mouse | FREQ 2.4G 鼠标接收器 | 0xA8A5:0x2255 | x23_freq | 2.4G | 否 |
| mouse | 狼蛛 V9 Pro | 0x373B:0x1058 | aula_v9 | 2.4G | 否 |

## 八、罗技鼠标全表（82 条）

来源：libratbag `DeviceType=mouse`，VID 固定 046D。G304 与 G305 是同硬件同 ID（4074）。

| PID | 型号 | 电池 |
|---|---|---|
| 0a89 | G635 | 内置/未知 |
| 101b | M705 | 换电池 |
| 1028 | M570 | 内置/未知 |
| 400a | M325 | 换电池 |
| 402c | G602 | 换电池 |
| 4041 | MX Master | 内置/未知 |
| 404a | MX Anywhere 2 | 内置/未知 |
| 4052 | M545 | 换电池 |
| 4053 | G900 | 内置/未知 |
| 405d | G403 Wireless | 内置/未知 |
| 405e | M720 | 换电池 |
| 4060 | MX Master | 内置/未知 |
| 4063 | MX Anywhere 2 | 内置/未知 |
| 4067 | G903 | 内置/未知 |
| 4069 | MX Master 2S | 内置/未知 |
| 406a | MX Anywhere 2S | 内置/未知 |
| 406b | M585/M590 | 换电池 |
| 406c | G603 | 换电池 |
| 406d | Marathon M705 | 换电池 |
| 406f | MX Ergo | 内置/未知 |
| 4070 | G703 | 内置/未知 |
| 4071 | MX Master | 内置/未知 |
| 4072 | MX Anywhere 2 | 内置/未知 |
| 4074 | G304 / G305 | 换电池 |
| 4079 | G Pro Wireless | 内置/未知 |
| 407b | MX Vertical | 内置/未知 |
| 407f | G502 Hero Wireless | 内置/未知 |
| 4082 | MX Master 3 | 内置/未知 |
| 4085 | G604 | 内置/未知 |
| 4086 | G703 Hero | 内置/未知 |
| 4087 | G903 Hero | 内置/未知 |
| 4090 | MX Anywhere 3 | 内置/未知 |
| 4093 | G Pro X Wireless Superlight | 内置/未知 |
| 4099 | G502 X PLUS | 内置/未知 |
| 409d | G705 | 内置/未知 |
| 409f | G502 X Wireless | 内置/未知 |
| 4101 | T650 | 内置/未知 |
| c041 | G5 | 换电池 |
| c048 | G9 | 换电池 |
| c049 | G5 | 换电池 |
| c066 | G9x [Original] | 换电池 |
| c068 | G500 | 换电池 |
| c06b | G700 | 换电池 |
| c077 | MX Vertical | 内置/未知 |
| c07c | G700s | 换电池 |
| c07d | G502 Proteus Core | 内置/未知 |
| c07e | G402 Gaming Mouse | 内置/未知 |
| c07f | G302 | 内置/未知 |
| c080 | G303 | 内置/未知 |
| c081 | G900 | 内置/未知 |
| c082 | G403 Wireless | 内置/未知 |
| c083 | G403 | 内置/未知 |
| c084 | G102/G103/G203 | 内置/未知 |
| c085 | G Pro | 内置/未知 |
| c086 | G903 | 内置/未知 |
| c087 | G703 | 内置/未知 |
| c088 | G Pro Wireless | 内置/未知 |
| c08a | MX Vertical | 内置/未知 |
| c08b | G502 Hero | 内置/未知 |
| c08c | G Pro | 内置/未知 |
| c08d | G502 Hero Wireless | 内置/未知 |
| c08e | MX518 | 换电池 |
| c08f | G403 Hero | 内置/未知 |
| c090 | G703 Hero | 内置/未知 |
| c091 | G903 Hero | 内置/未知 |
| c092 | G102/G103/G203 | 内置/未知 |
| c093 | M500s | 内置/未知 |
| c094 | G Pro X Wireless Superlight | 内置/未知 |
| c095 | G502 X PLUS | 内置/未知 |
| c096 | G705 | 内置/未知 |
| c097 | G303 Shroud Edition | 内置/未知 |
| c098 | G502 X Wireless | 内置/未知 |
| c099 | G502 X | 内置/未知 |
| c09d | G102/G103/G203 | 内置/未知 |
| c246 | G300 | 换电池 |
| c249 | G9x [Call of Duty MW3 Edition] | 换电池 |
| c24a | G600 | 内置/未知 |
| c24e | G500s | 换电池 |
| c332 | G502 Proteus Spectrum | 内置/未知 |
