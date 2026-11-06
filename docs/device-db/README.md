> 设备目录收录不等于当前协议已启用或实机通过；逐项策略与D盘官方来源复核见 [2026-11-06支持审计](../SUPPORT-AUDIT-20261106.md)。

# MouseDock 设备数据库

鼠标的 VID/PID、协议族和电量读取方式。给人看的、给程序吃的、给下游项目并库的，各一份。

## 文件

| 文件 | 用途 |
|---|---|
| `mouse-db.md` | 全表，七节，含缺口清单 |
| `mouse-db.json` | 机读全量，readers / devices / oss / receivers 四段 |
| `mouse-db.csv` | 单表，Excel 可筛 |
| `devices-merged.json` | 与源码 `devices.json` 同构的条目，直接并库用 |

## 数据来源

- `notes/battery-readers.md`：商业版 MouseDock 的 `battery_*.py` 字节码逆向，35 条帧级协议。带「未确认」的就是没反出来，别当事实用。
- `extracted/devices.json`：商业版身份库 v143，26 条设备 + brand_ids / family_rules / auto_families。
- `notes/receivers.csv`：接收器和无线外设 185 条，其中鼠标 88 条。
- 上游开源库三个，只取事实数据：libratbag、OpenRazer、Solaar。它们的代码是 GPL，这里只用设备识别用的 VID/PID 与协议族归类。

## devices-merged.json 的字段

```json
{
  "vid": "0x3837",
  "pid": "0x1021",
  "model": "mchose_mhub",
  "name": "MCHOSE A7",
  "battery": "mchose_mhub",
  "page": "",
  "usage": null,
  "connection": "2.4G",
  "verified": false,
  "source": "notes/battery-readers.md",
  "aliases": ["同 VID:PID 的其他型号名"]
}
```

`battery` 是协议族名，对应 `battery-readers.md` 里的 reader。空字符串表示只知道 ID、不知道电量怎么读。

## 更新

```powershell
& D:\mousedock\py312\python.exe D:\mousedock\fetch_devdb.py    # 拉上游三个仓库
& D:\mousedock\py312\python.exe D:\mousedock\parse_devdb.py    # 解析成 mouse-device-db.json
& D:\mousedock\py312\python.exe D:\mousedock\build_mouse_db.py # 合并出四份产物
```

## 缺口

只有 VID/PID、没有帧格式的：Corsair、ROCCAT、微星、技嘉、酷冷、HyperX、微软、苹果、卓威。

一条都没收的：黑爵 AJ、前行者（只有接收器侧的 X23 系列）、红龙 M 系列、富勒、血手幽灵、多彩、Keychron、Glorious、Xtrfy、Endgame Gear、Vaxee、Pwnage、Zaunkoenig、Kensington、3Dconnexion、Elecom。

达尔优只有接收器侧的 0x260D，鼠标本体的 VID:PID 没有公开记录，别拿 0x260D 当鼠标 ID 用。
