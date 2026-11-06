# G87 V2 / Logitech Lightspeed 适配证据与缺口

本轮仅完成官方来源静态核对，未启用新的设备查询或宣称电量适配完成。当前电脑未枚举到目标设备。

## G87 V2

官方网页驱动main-DCRwBIvm.js SHA256：`405102805e2adeb93073ee2186f6d0193ca1317a6b4657d636e4e611bd25a446`。

- 明确产品键：MCHOSE G87 V2 (1K)，VID14391/PID8218，即3837:201A；MCHOSE 2.4G Wireless-G87_V2，VID14391/PID8219，即3837:201B。
- 过滤页65282，即FF02；仅页值不能证明集合长度、usage或实际报告能力。
- diffRID[G87_V2]指定有线report9、无线report19，不能套用G7鼠标的report0/65路径。
- getBattery有线模板为06 87 00 00 01 00 02 00后512个零；驱动去掉模板首字节，由实际report9调用sendFeatureReport，再receiveFeatureReport。transformHandler默认切去2字节，getBatteryParser的有线分支跳6字节后读电量及两个4位状态。
- 无线模板13 4A 01后零填充，追加字节累加校验；sendReport之后用commandParams、current和total匹配分包，合并每包slice(4,-1)后再解析电量。不能从任意原始响应固定位置直接取百分比。
- 状态位解析存在，但当前尚未独立确认所有状态枚举与充电语义，不猜充电字段。

需要新版设备报告中的实际产品串、连接模式、usage、集合长度和report能力；还需确认请求关联、分包长度、校验和与响应头，之后才能加入精确实例读取及回归测试。

## Logitech Lightspeed

Lightspeed是接收器族名称，不是唯一外设型号。当前初代GPW专用路径只接受C539/C088并要求实时型号身份，之后才能使用初代GPW电压模型。不得把其他Lightspeed鼠标或键盘套成GPW。

需要用户确认具体销售型号，或提供新版设备报告的VID/PID、集合和身份响应。未取得这些资料前不扩大主动查询范围，不把名字或接收器编号当作已连接外设身份。

## 2026-11-06新增证据与当前测试版

用户确认罗技为GPW2，照片报告为046D:C547；G87 V2无线为3837:3033，要求同时研究有线。

GPW2：从既有官方G HUB details.json定位pro_x_wireless_mouse.depot，下载到D盘后静态解包，未运行安装器。官方battery/battery.xml标识046D:4093、容量0.874Wh，仅包含功耗结构，不提供一代GPW的放电曲线，因此不共用4079曲线。

测试版接入C547唯一FF00、20/20/0集合，沿用严格HID++槽位/特征/函数/软件ID相关、排空旧队列、实例隔离与总预算。两次完整实时名称确认GPW2后才查询0x1004；能力声明支持百分比，再读取两次状态。0..100为设备百分比，拒绝异常状态或错误数据。未提供1004的固件继续显示未知，不改用猜测电压模型。尚未进行GPW2实机验证。

1004字段依据： https://raw.githubusercontent.com/torvalds/linux/master/drivers/hid/hid-logitech-hidpp.c ，只使用协议字段事实，未移植其实现。官方驱动包： https://updates.ghub.logitechg.com/depots/c1fd34b6-1215-48c2-8b0e-525cdcd010f2/pro_x_wireless_mouse.depot

G87 V2：当前官方入口 https://www.mchose.com.cn/cizhou/CZ_SHARED_DATA/ 指向main.5c506fa56ff95a9a68ef.js，已保存D盘。注册表确认8K版有线3837:2015，接收器3837:3033且必须匹配产品串MCHOSE G87 V2 2.4G；3033同时用于G75 V2/K87S，绝不能按PID套型号。测试版仅修正键盘分类，没有把3033套用201A/201B的1K版电量路径。

G87 V2电量尚未接入：新版SDK的GLW getFuncConfig读配置命令5，每次分片最多56字节；仍需继续核对所选型号的结构解析、报文头、校验、报告编号与只读事务。找到某个函数中的rf_battery字段不足以证明适用于所有型号，不能先套偏移。

本测试版包含此前悬浮图层和任务栏尺寸修正。当前完成GPW2有限百分比读取与G87 V2分类；两种G87 V2电量读取仍未完成。临时文件与D盘分析资料保留，未替换线上Release。

来源文件 pro_x_wireless_mouse.depot SHA256: 59882d8f2ab276fb25ac91a21e1ed66908692d5345a449be192e7fd8f794f9ef

来源文件 cz-shared.js SHA256: f7913985e0055bc5b36c7a02f9ae3de02e1c422a56036b57558c9e24b42234ea
