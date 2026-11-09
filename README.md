# MEP_Radial_Routing_ver1

Add-in C# / WPF cho **Revit 2023, .NET Framework 4.8, x64**. Đi tuyến Pipe bằng radial menu, preview DirectContext3D, xuất Pipe Placeholder. Giữ tên file project cũ để không phá solution hiện có; assembly mới là `MEP_Radial_Routing_ver1.dll`. `Class1.cs` ban đầu vẫn giữ nhưng không nằm trong build.

## Build và cài đặt

Cần Visual Studio có .NET desktop development + .NET Framework 4.8 targeting pack và Revit 2023. Không cần NuGet.

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\MEP_Direct_Context_3D_ver1.csproj /t:Build /p:Configuration=Release /p:UseSharedCompilation=false
# Nếu API nằm chỗ khác, thêm: '/p:RevitApiPath=D:\Autodesk\Revit 2023'
.\scripts\test.ps1
.\scripts\install.ps1 -Configuration Release
```

`install.ps1` ghi manifest vào `%APPDATA%\Autodesk\Revit\Addins\2023`, trỏ tới DLL trong thư mục build hiện tại. Chạy script trên máy cần cài, sau khi build. Đóng/mở lại Revit; chọn **Add-Ins → MEP Radial Routing → Pipe Radial Routing**. Khi di chuyển source/build sang chỗ khác, chạy lại script. Muốn gỡ, xóa riêng `MEP_Radial_Routing_ver1.addin` ở thư mục Addins/2023 khi Revit đã đóng. Không chép RevitAPI.dll/RevitAPIUI.dll cùng add-in.

Manifest ở thư mục gốc là template; không cài trực tiếp khi còn `__ASSEMBLY_PATH__`. Bản manifest cụ thể dùng DLL Release hiện tại được đặt trong `bin/Release`.

## Cách dùng

1. Mở project trong view 3D **orthographic**, chưa hỗ trợ perspective/family document.
2. Chọn Select source element rồi chọn Pipe hoặc FamilyInstance có connector End, round, DomainPiping chưa kết nối. Danh sách hiển thị ID, tọa độ mm và đường kính. Dấu đỏ là connector chọn; dấu xám là các connector còn lại. Đổi connector trong danh sách để xem highlight.
3. Kiểm tra System Type, Pipe Type, Level, Diameter mm và Reference Frame. Pipe nguồn cung cấp mặc định type/system/level nếu có; thiết bị dùng các lựa chọn khả dụng và level gần nhất khi không có level. Diameter lấy theo connector đang chọn. Kiểm tra lại system khi dùng thiết bị.
4. Start Routing. Bảng thiết lập được khóa trong phiên để frame và thông số không đổi giữa tuyến.
5. Đặt chuột trong viewport, giữ Ctrl để mở bánh xe tại con trỏ. Tâm cố định trong lần mở. Rê chọn góc; wheel đổi XY/XZ/YZ hoặc UV/UW/VW. Thả Ctrl trên vùng để khóa hướng; thả ở tâm/ngoài vòng hoặc Esc để giữ hướng, mặt phẳng và preview trước đó.
6. Di chuyển chuột để xác định chiều dài trên hướng model đã khóa. Chiều dài/lỗi hiển thị trong bảng thiết lập. Hoặc nhập Exact length (mm), bấm Apply length; chế độ nhập chính xác giữ nguyên chiều dài khi di chuyển chuột tới lúc khóa hướng mới. Đoạn không hợp lệ màu đỏ.
7. Click trái trong viewport hoặc Enter để chốt. Endpoint trở thành origin mới. Ctrl tiếp tục chọn hướng. Backspace xóa đoạn chốt cuối; Esc bỏ đoạn đang preview. Cancel bỏ toàn bộ phiên.
8. Custom angle nhận số độ theo mặt phẳng đang hoạt động; bấm Lock custom angle để khóa. Chọn lại mặt phẳng qua radial menu nếu cần. Preset radial cố định 8 góc mỗi 45°; không có trình sửa danh sách preset trong v1.
9. Finish chỉ xuất các đoạn đã chốt. Nếu còn đoạn preview, chốt hoặc Esc trước. Khi Revit từ chối tạo/liên kết placeholder, transaction rollback và tuyến vẫn được giữ để Undo/chỉnh lại. Thành công sẽ chọn placeholder vừa tạo và dọn toàn bộ preview.

**Placeholder bắt đầu tại tọa độ connector nguồn nhưng chưa kết nối vào nguồn. Chưa chuyển thành Pipe thật và chưa xác minh fitting theo routing preferences.** Liên kết placeholder tại góc dùng `PlumbingUtils.ConnectPipePlaceholdersAtElbow`; các đoạn thẳng liên tiếp dùng connector `ConnectTo`. Lỗi liên kết làm rollback toàn bộ; không tự bỏ qua lỗi để giữ các đoạn rời.

## Quy ước hình học

- Project dùng trục tọa độ nội bộ model X/Y/Z; không xoay theo Project North / True North hoặc shared coordinates. Đây là nghĩa của Project trong bản này.
- Connector: U = BasisZ hướng ra connector; V = Z × U, trừ khi gần song song Z thì dùng Y × U; W = U × V. Frame giữ nguyên suốt phiên, chỉ di chuyển origin.
- UV/XY: 0° theo U/X, +90° theo V/Y. UW/XZ: 0° theo U/X, +90° theo W/Z. VW/YZ: 0° theo V/Y, +90° theo W/Z.
- Hướng = cos(a) A + sin(a) B. Bánh xe nằm trên màn hình; hướng trên bánh xe **không phải** hướng chiếu của model.
- Screen projection dùng `UIView.GetWindowRectangle`, `GetZoomCorners`, `View.RightDirection/UpDirection`; chiếu ray hướng đã khóa lên hai trục màn hình và giải bình phương tối thiểu để tìm L. Không dùng PickPoint cho mouse-move.
- Hướng có độ dài chiếu nhỏ hơn 0.08 (~4.6° từ hướng nhìn) không kéo chiều dài; nhập số hoặc đổi camera/hướng. L âm không được chốt. Giới hạn tối thiểu max(ShortCurveTolerance, 0.1 inch) + tolerance.
- Đoạn trùng/chồng ngược với bất kỳ đoạn nào đã chốt bị từ chối. Không kiểm tra va chạm vật cản, tự giao không đồng tuyến, hoặc khả năng đặt fitting.

## Kiến trúc và vòng đời

- `Core/Geometry.cs`: Vec, RoutingFrame, Projection, RadialHitTest (dead zone 38 DIP, hysteresis 3.5°), RouteSession/RouteSegment và state transitions. Không tham chiếu Revit/WPF.
- `Revit/Routing.cs`: command, ribbon application, controller, ExternalEvent request handler, Idling snapshot/refresh. API document chỉ chạy trong command, ExternalEvent, Idling hoặc lifecycle callbacks hợp lệ.
- `Revit/Services.cs`: lọc/chọn connector, snapshot identity, revalidate nguồn, tạo placeholder trong transaction và failure preprocessor rollback.
- `Revit/PreviewRenderer.cs`: DirectContext3D server, snapshot stroke bất biến; buffer được tạo/dispose trong RenderScene, chia batch theo giới hạn chỉ số. Wireframe đường kính đơn giản, centerline, mũi tên, trục và khung mặt phẳng tạm. Không tạo ModelLine/ReferencePlane.
- `UI/Views.cs`: WPF settings và radial overlay không lấy focus. Tính vị trí bằng screen coordinates và effective DPI transform của WPF.
- `UI/InputController.cs`: hook keyboard/mouse Win32 chỉ cài khi Start Routing. Callback không gọi Revit document API; chỉ đổi dữ liệu trong bộ nhớ. Bỏ qua input khi Revit main window không có foreground, ngoài viewport (trừ lúc menu mở), hoặc không ở phiên routing. Wheel chỉ chặn khi Ctrl/menu đang mở.
- Idling chụp viewport/camera; chỉ RefreshActiveView khi dữ liệu preview thay đổi. Không vòng redraw liên tục. Renderer không đọc UI/session đang bị sửa từ render thread.
- Finish/Cancel/đóng document/thoát Revit đều unhook, dừng timer, đóng overlay, bỏ subscription và unregister graphics server. Mất focus đóng menu và giữ trạng thái trước; quay lại Revit để tiếp tục. Đổi sang view/document khác tạm ngưng input tới khi trở lại view gốc.

## Kiểm chứng đã thực hiện

Ngày 09/11/2026:

- Debug build thành công với DLL thực tế trong `C:\Program Files\Autodesk\Revit 2023`.
- Release rebuild thành công, không warning/error; DLL trong `bin/Release`.
- Test console độc lập Revit: **168 assertions PASS** gồm connector frame ngang/đứng/nghiêng, 8 góc cả 3 mặt phẳng, hướng +90°, radial dead zone/hysteresis, projection có offset màn hình âm, hướng gần camera, minimum length, overlap, Undo/Esc, commit/rollback/complete/cancel.
- Chữ ký `Pipe.CreatePlaceholder`, `ConnectPipePlaceholdersAtElbow`, `DrawContext.FlushBuffer` được đối chiếu XML API Revit 2023 cài trên máy. DirectContext3D buffer/server interfaces được compiler kiểm tra với DLL 2023.

**Chưa chạy kiểm thử tương tác trong Revit.** Build không chứng minh hook, hiển thị GPU, DPI hoặc transaction tạo placeholder chạy đúng với từng model. Chưa cài manifest vào hồ sơ Revit trên máy; chỉ tạo source, build và hướng dẫn cài.

## Checklist kiểm thử thủ công trong Revit 2023

Dùng bản sao model MEP có System Type, Pipe Type, Level và thiết bị/pipe với connector mở.

| Ca kiểm tra | Thao tác / kết quả mong đợi |
|---|---|
| Điều kiện view | Perspective/plan/family: command từ chối; orthographic 3D: mở bảng |
| Nguồn ngang/đứng/nghiêng | Chọn từng nguồn, kiểm tra marker đúng vị trí và đường kính; connector đã kết nối/khác domain/non-round không chọn được |
| Nhiều connector | Đổi từng dòng danh sách; marker đỏ và diameter đổi theo connector |
| Frame/góc | Project và Connector; thử 0/45/90/180/270 trên cả ba mặt phẳng; đối chiếu trục màu và quy ước trên |
| Ctrl / tâm / Esc | Giữ Ctrl mở, hover cập nhật mũi tên; tâm cố định; thả tâm/ngoài vòng/Esc khôi phục plane và preview cũ; sát ranh giới không nhấp nháy |
| Wheel / focus | Wheel mở menu đổi plane; wheel thường zoom Revit. Alt-Tab lúc giữ Ctrl: menu đóng; ứng dụng khác không bị bắt phím/wheel |
| Chiều dài kéo | So sánh chiều dài hiển thị với khoảng cách model; quay camera không đổi vector model đã khóa |
| Hướng camera | Khóa hướng gần view-normal, thử kéo: không tạo điểm tùy tiện; nhập 1000 mm vẫn tạo đúng đoạn |
| Nhập số | Custom 22.5°, length 1000 mm, Apply; di chuột không đổi exact length; số âm/NaN/độ dài quá ngắn bị từ chối |
| Liên tục/Undo/Esc | Chốt 3 đoạn, Backspace trả origin về endpoint đoạn 2; Esc chỉ bỏ preview; đoạn đã chốt vẫn hiển thị |
| Hình học lỗi | Quay ngược chồng đoạn trước hoặc quay lại trùng đoạn cũ: đỏ + báo lỗi, không chốt |
| Finish thành công | Finish 2–3 đoạn hợp lệ: placeholder đúng type/system/level/diameter, liên kết nhau, được chọn; nguồn chưa được kết nối |
| Finish lỗi | Trong bản sao model, đổi/xóa nguồn hoặc làm kết nối nguồn trong khi phiên đang mở: Finish báo lỗi, không có phần tử dở dang, tuyến được giữ; thử thêm tuyến có góc Revit không chấp nhận |
| Cleanup | Cancel, Finish, đóng document trong phiên, thoát Revit; không còn preview/overlay, Ctrl và wheel trở lại bình thường; chạy lại command |
| DPI/nhiều màn hình | 100/125/150/200%, màn hình bên trái có tọa độ âm, kéo cửa sổ Revit sang monitor khác: tâm đúng chuột, vùng hover đúng sector; lặp thao tác zoom/pan |
| View/document | Chuyển view hoặc document: routing input tạm ngưng; về view gốc tiếp tục; đóng document nguồn dọn phiên |

## Giới hạn cần lưu ý

Đây là bản triển khai v1 cần qua checklist Revit trước khi dùng sản xuất. Wireframe preview chưa phải shaded solid và chưa có HUD chiều dài tại con trỏ; chiều dài nằm trong bảng thiết lập. Preset wheel cố định 45°, custom angle nhập ở bảng. Không hỗ trợ linked model connector, perspective, duct/tray/conduit, persistence/resume sau khi đóng phiên, obstacle avoidance, hoặc conversion thành Pipe thật. System của connector thiết bị cần người dùng kiểm tra thủ công. Không thực hiện kiểm tra fitting feasibility.

Hook phụ thuộc Win32/WPF trong tiến trình Revit; DPI hỗn hợp phụ thuộc DPI awareness của host và cần kiểm chứng thực tế. Menu sát mép màn hình có thể bị cắt vì giữ tâm tại chuột. Khi phiên routing đang hoạt động, Ctrl/Enter/Esc/Backspace và click chốt trong viewport dành cho công cụ này; Finish/Cancel trước khi chuyển sang một lệnh Revit khác. Thay đổi document từ add-in khác chỉ được kiểm tra lại nguồn ở Finish. Mã hiện tại tách core để mở rộng loại MEP sau này, chưa cung cấp backend cho các loại khác.

## Tài liệu API

- XML đi kèm Revit 2023: `RevitAPI.xml`, `RevitAPIUI.xml` là nguồn đối chiếu theo đúng phiên bản build.
- [Autodesk — Displaying Graphics with DirectContext3D](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Views/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_Displaying_Graphics_with_DirectContext3D_html.html) (tham khảo kiến trúc; không dùng để suy đoán chữ ký 2023).
- [Autodesk — Placeholders](https://help.autodesk.com/cloudhelp/2024/PTB/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/MEP_Engineering/MEP_Element_Creation/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_MEP_Engineering_MEP_Element_Creation_Placeholders_html.html).
