#include "cursor_pixels.h"
#include <iostream>
static void require(bool ok,const char* message) {if(!ok)throw std::runtime_error(message);}
int main() {
    try {
        std::vector<uint8_t> mask(8),color{0,0,255,128,0,255,0,255};
        magpie::CursorPixels alpha(2,1,color,mask);
        auto blue=[](size_t count) {std::vector<uint8_t> result(count*4);for(size_t i=0;i<count;++i){result[i*4+2]=255;result[i*4+3]=255;}return result;};
        auto nearest=blue(4);alpha.composite(nearest,4,1,0,0,4,1,false);
        require(nearest==std::vector<uint8_t>({128,0,127,255,128,0,127,255,0,255,0,255,0,255,0,255}),"Nearest alpha cursor or premultiplication differs");
        auto linear=blue(4);alpha.composite(linear,4,1,0,0,4,1,true);
        require(linear==std::vector<uint8_t>({128,0,127,255,96,64,95,255,32,191,32,255,0,255,0,255}),"Bilinear clamped pixel-centre premultiplied sampling differs");
        auto clipped=blue(3);alpha.composite(clipped,3,1,-1,0,4,1,true);
        require(std::equal(clipped.begin(),clipped.end(),linear.begin()+4),"Clipping rescaled or shifted cursor texture coordinates");
        auto unchanged=blue(3),outside=unchanged;alpha.composite(outside,3,1,INT32_MIN,0,4,1,true);
        require(outside==unchanged,"Offscreen cursor touched unrelated pixels");
        std::vector<uint8_t> monoMask{255,255,255,0,255,255,255,0,255,255,255,0,0,0,0,0};
        magpie::CursorPixels mono(2,1,{},monoMask);
        auto a=blue(4),b=a;mono.composite(a,4,1,0,0,4,1,false);mono.composite(b,4,1,0,0,4,1,true);
        require(a==b&&a==std::vector<uint8_t>({255,255,0,255,255,255,0,255,0,0,255,255,0,0,255,255}),"Monochrome XOR was filtered or lost background inversion");
        std::vector<uint8_t> maskedColor{0xaa,0xf0,0x0f,0,33,22,11,0},maskedMask{255,255,255,0,0,0,0,0};
        magpie::CursorPixels masked(2,1,maskedColor,maskedMask);
        a=blue(4);b=a;masked.composite(a,4,1,0,0,4,1,false);masked.composite(b,4,1,0,0,4,1,true);
        require(a==b&&a[0]==0x0f&&a[1]==0xf0&&a[2]==0x55&&a[8]==11&&a[9]==22&&a[10]==33,"Masked-color point/XOR composition differs");
        std::vector<uint8_t> convertible{0,0,0,0,255,255,255,0,255,255,255,0,0,0,0,0};
        magpie::CursorPixels converted(2,1,{},convertible);a=blue(4);converted.composite(a,4,1,0,0,4,1,true);
        require(a[4]==191&&a[5]==191&&a[6]==255,"Convertible monochrome cursor did not receive alpha interpolation");
        std::vector<uint8_t> squareColor{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255},squareMask(16);
        magpie::CursorPixels square(2,2,squareColor,squareMask);auto grid=blue(9);
        square.composite(grid,3,3,0,0,3,3,true);
        require(grid==std::vector<uint8_t>({255,0,0,255,128,128,0,255,0,255,0,255,
            128,0,128,255,128,128,128,255,128,255,128,255,
            0,0,255,255,128,128,255,255,255,255,255,255}),"Vertical orientation or two-axis bilinear weights differ");
        bool invalid=false;try {alpha.composite(a,4,1,0,0,0,1,false);}catch(const std::invalid_argument&){invalid=true;}
        require(invalid,"Invalid cursor output size accepted");
        std::cout<<"CURSOR PIXELS PASS: premultiplied alpha, clamped nearest/bilinear pixel centres, clipped texture coordinates, color/monochrome XOR and convertible masks.\n";
    }catch(const std::exception& ex){std::cerr<<ex.what()<<'\n';return 1;}
}
