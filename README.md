# Silver Wolf Launcher 🐺
*Advanced Launcher for Honkai: Star Rail Private Servers*

![Silver Wolf Launcher Preview](Assets/preview.png)

**Silver Wolf Launcher** คือเครื่องมือระดับพรีเมียมที่ออกแบบมาเพื่อให้การจัดการและเข้าเล่น **Honkai: Star Rail Private Server** เป็นเรื่องง่าย สวยงาม และทรงพลัง พัฒนาด้วยเทคโนโลยีล่าสุดอย่าง **.NET 10.0** และ **WPF**

> [!IMPORTANT]
> **Project Status**: ปัจจุบันอยู่ในสถานะ **Early Access** ผลงานชิ้นนี้พัฒนาโดยอิสระเพื่อชุมชน ยังไม่มีกำหนดการปล่อยเวอร์ชันอย่างเป็นทางการ และหากพบปัญหาท่านสามารถแจ้งได้ที่ [Discord Community](https://discord.gg/QwfTnEdAtN)

---

## ✨ คุณสมบัติที่โดดเด่น (Key Features)

*   **🎨 Premium HSR Aesthetic**: อินเทอร์เฟซแบบ Glassmorphism สวยงามและลื่นไหล
*   **⚡ One-Click Launch**: ระบบเริ่มเกมรวม Proxy และ Server ในคลิกเดียว
*   **🔄 Integrated Auto-Update**: ระบบตรวจสอบและอัปเดตตัวเองอัตโนมัติผ่าน GitHub
*   **🛡️ Robust Watchdog**: ระบบเฝ้าระวังและรีสตาร์ทบริการอัตโนมัติภายใน 5 วินาที
*   **🚫 Single Instance Only**: ระบบป้องกันการรันโปรแกรมซ้อนกัน เพื่อความเสถียรสูงสุด
*   **🌐 Seamless Integration**: รวมเครื่องมือเสริม (SR Tools) และตัว Patch ไฟล์เกมไว้ที่เดียว
*   **🔘 Smart Play Button**: ปุ่มเริ่มเกมจะปรับเปลี่ยนสถานะตามความถูกต้องของ Path เกม

---

## 🛠️ การติดตั้งและเริ่มต้นใช้งาน (Getting Started)

### สำหรับผู้ใช้งานทั่วไป (Users)
*   **Download**: ดาวน์โหลดไฟล์ `SilverWolfLauncher.exe` จากหน้า **Releases**
*   **Update**: เมื่อมีเวอร์ชันใหม่ จะมีปุ่ม **UPDATE NOW** ปรากฏขึ้นในหน้า Home

### สำหรับนักพัฒนา (Developers)
*   **Dev Build**: รันโค้ดผ่าน Visual Studio หรือใช้คำสั่ง `dotnet build` เพื่อได้ไฟล์แบบแยกโฟลเดอร์ปกติ (เหมาะสำหรับการ Debug)
*   **Release Build**: การรันไฟล์เดียว (Single File) **เฉพาะสำหรับแจกจ่ายเท่านั้น**
    *   รันสคริปต์: `Build-Release.ps1`
    *   หรือใช้คำสั่ง: `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./Publish`

---

## 📜 ประวัติการอัปเดต (Version History)
ท่านสามารถดูรายละเอียดการเปลี่ยนแปลงและ Update Details ทั้งหมดได้ที่ไฟล์ [CHANGELOG.md](CHANGELOG.md)

---

## 🛡️ ความปลอดภัยและความเป็นส่วนตัว (Security & Privacy)
*   **No Data Collections**: เราไม่มีการเก็บข้อมูลส่วนตัวใดๆ ไฟล์โปรไฟล์ถูกเก็บไว้ในเครื่องผู้ใช้เท่านั้น

---

**Crafted with 💖 by xeroxua**
*Disclaimer: Silver Wolf Launcher เป็นโปรเจกต์อิสระ ไม่ได้มีความเกี่ยวข้องกับ HoYoverse*
